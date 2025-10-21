// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.SessionConnection
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using EnumsNET;
using FrooxEngine;
using FrooxEngine.Store;
using Renderite.Shared;
using SkyFrost.Base;

public class SessionConnection : IDisposable
{
	public enum State
	{
		WaitingForJoinRequest,
		JoinChallengeSent
	}

	private byte[] hostVerificationToken;

	public byte[] identityHash;

	public byte[] machineIdentitySignature;

	public byte[] cloudIdentitySignature;

	private string banAccessKey;

	private Task<CloudResult<RSAParametersData>> publicKeyTask;

	private Task<CloudResult<SkyFrost.Base.User>> userDataTask;

	private TaskCompletionSource<ControlMessage> _controlMessage = new TaskCompletionSource<ControlMessage>();

	private TaskCompletionSource<ControlMessage> _joinContactChallengeReady;

	private int _connectionClosed;

	public SessionConnectionManager Connections { get; private set; }

	public SessionMessageManager Messages => Connections.Messages;

	public Session Session => Connections.Session;

	public SessionSyncManager Sync => Session.Sync;

	public SessionCryptoHelper Crypto => Session.Crypto;

	public Engine Engine => Session.Engine;

	public LocalDB LocalDB => Engine.LocalDB;

	public World World => Connections.World;

	public SkyFrostInterface Cloud => World.Engine.Cloud;

	public IConnection Connection { get; private set; }

	public FrooxEngine.User User { get; private set; }

	public State ConnectionState { get; private set; }

	public bool IdentityVerified { get; private set; }

	public SkyFrost.Base.User CloudUser { get; private set; }

	public string MachineID { get; private set; }

	public string UserID { get; private set; }

	public string UserSessionID { get; private set; }

	public string Username { get; private set; }

	public RSAParameters MachinePublicKey { get; private set; }

	public Platform Platform { get; private set; }

	public HeadOutputDevice HeadDevice { get; private set; }

	public bool HasEyeTracking { get; private set; }

	public bool HasPupilTracking { get; private set; }

	public bool HasFaceTracking { get; private set; }

	public List<MouthParameterGroup> MouthTrackingParameters { get; private set; }

	public DataTreeList DeviceInfos { get; private set; }

	public bool KioskMode { get; private set; }

	public bool SpectatorBan { get; internal set; }

	public bool MuteBan { get; internal set; }

	public string RoleOverride { get; set; }

	public Dictionary<string, string> ExtraIDs { get; private set; }

	public SessionConnection(SessionConnectionManager connections, IConnection connection)
	{
		Connections = connections;
		Connection = connection;
		connection.Closed += OnConnectionClosed;
		if (!connection.IsOpen)
		{
			OnConnectionClosed(Connection);
		}
	}

	private void OnConnectionClosed(IConnection connection)
	{
		if (Interlocked.CompareExchange(ref _connectionClosed, 1, 0) == 0)
		{
			DisconnectMessage msg = new DisconnectMessage(connection);
			Sync.EnqueueForSyncProcessing(msg);
		}
	}

	internal bool ProcessJoinControlMessage(ControlMessage message)
	{
		if (message.ControlMessageType == ControlMessage.Message.JoinContactChallengeReady)
		{
			_joinContactChallengeReady.SetResult(message);
			return true;
		}
		return _controlMessage.TrySetResult(message);
	}

	private async Task<ControlMessage> ReceiveResponse(params ControlMessage.Message[] expectedTypes)
	{
		ControlMessage response = await _controlMessage.Task.ConfigureAwait(continueOnCapturedContext: false);
		if (!expectedTypes.Contains(response.ControlMessageType))
		{
			throw new SessionRejectJoinException("World.Error.SecurityViolation", $"Expecting control message of following types: {string.Join(", ", expectedTypes)}, but received {response.ControlMessageType} instead", tempBan: true);
		}
		_controlMessage = new TaskCompletionSource<ControlMessage>();
		return response;
	}

	public async Task<string> RequestContactCheckKey()
	{
		if (_joinContactChallengeReady != null)
		{
			throw new InvalidOperationException("Contact check key was already requested for this connection");
		}
		_joinContactChallengeReady = new TaskCompletionSource<ControlMessage>();
		ControlMessage challenge = new ControlMessage(ControlMessage.Message.JoinContactChallenge);
		challenge.Targets.Add(Connection);
		string keyBase = CryptoHelper.GenerateCryptoToken(64);
		challenge.Data.Add("CheckContactKeyBase", keyBase);
		Messages.Outgoing.EnqueueForTransmission(challenge, isOrigin: true, useBackgroundEncoder: false);
		string keyId = (await _joinContactChallengeReady.Task).Data.ExtractOrDefault<string>("CheckContactKeyId");
		if (keyId != null && !keyId.Contains(keyBase))
		{
			UniLog.Warning("Key doesn't contain requested base ID! BaseId: " + keyBase + ", KeyId: " + keyId);
			keyId = null;
		}
		return keyId;
	}

	public void StartJoinHandshake(ControlMessage joinRequest)
	{
		if (!Connection.IsOpen)
		{
			return;
		}
		if (!World.IsAuthority)
		{
			Connections.TriggerFatalFailure(joinRequest.Sender, "Trying to start host join handshake process on a non-host");
			return;
		}
		World.Coroutines.StartTask(async delegate
		{
			try
			{
				if (joinRequest.ControlMessageType != ControlMessage.Message.JoinRequest)
				{
					throw new InvalidOperationException($"Expected JoinRequest message, but instead got: {joinRequest.ControlMessageType}");
				}
				if (joinRequest.Sender != Connection)
				{
					throw new InvalidOperationException("JoinRequest is from different connection than this instance belongs to");
				}
				await PerformJoinHandshake(joinRequest);
			}
			catch (SessionRejectJoinException ex)
			{
				UniLog.Log($"Rejected join for connection {Connection} for user {UserID} {Username}. Reason: {ex.RejectReason}, TempBan: {ex.TempBan}\nDetail: {ex.Message}");
				if (ex.TempBan)
				{
					BanManager.TempBanConnection(Connection);
				}
				RejectJoin(ex.RejectReason);
			}
			catch (Exception value)
			{
				UniLog.Error($"Unhandled exception when performing join handshake for {Connection} for user {UserID} {Username}:\n{value}");
				RejectJoin("World.Error.UnhandledError");
			}
		});
	}

	private async Task PerformJoinHandshake(ControlMessage joinRequest)
	{
		if (BanManager.IsTempBanned(Connection))
		{
			throw new SessionRejectJoinException("World.Error.SecurityViolation", "User is temporarily banned");
		}
		if (!World.HasFreeUserCapacity)
		{
			throw new SessionRejectJoinException("World.Error.UserLimitReached", "World has ran out of capacity for users");
		}
		CheckExpectedSessionId(joinRequest);
		ExtractUserData(joinRequest);
		await SendJoinChallenge();
		await VerifyJoinIdentity(await ReceiveResponse(ControlMessage.Message.JoinAuthenticate));
		await default(ToSync);
		JoinGrant grant = await World.VerifyJoinRequest(this);
		if (!grant.granted)
		{
			throw new SessionRejectJoinException(grant.rejectReason, "World has rejected user join request");
		}
		await default(ToSync);
		if (Connection.IsOpen)
		{
			if (!World.HasFreeUserCapacity)
			{
				throw new SessionRejectJoinException("World.Error.UserLimitReached", "World has no free user capacity");
			}
			UniLog.Log("Join Granted For UserID: " + UserID + ", Username: " + Username);
			GrantJoinAndCreateUser();
			await ReceiveResponse(ControlMessage.Message.JoinRequestStartStreams);
			User.StartTransmittingStreamData();
		}
	}

	private void CheckExpectedSessionId(ControlMessage joinRequest)
	{
		string expectedSessionId = joinRequest.Data.ExtractOrDefault<string>("ExpectedSessionId");
		if (expectedSessionId == null || World.SessionId.Equals(expectedSessionId, StringComparison.InvariantCultureIgnoreCase))
		{
			return;
		}
		throw new SessionRejectJoinException("World.Error.SessionEnded", "Expected session ID is different");
	}

	private void ExtractUserData(ControlMessage joinRequest)
	{
		try
		{
			MachineID = joinRequest.Data.ExtractOrThrow<string>("MachineID");
			MachinePublicKey = new RSAParameters
			{
				Exponent = Convert.FromBase64String(joinRequest.Data.ExtractOrThrow<string>("MachinePublicKeyExponent")),
				Modulus = Convert.FromBase64String(joinRequest.Data.ExtractOrThrow<string>("MachinePublicKeyModulus"))
			};
			UserID = joinRequest.Data.ExtractOrDefault<string>("UserID");
			UserSessionID = joinRequest.Data.ExtractOrDefault<string>("UserSessionID");
			Username = joinRequest.Data.ExtractOrDefault<string>("UserName");
			HeadDevice = joinRequest.Data.ExtractOrThrow<HeadOutputDevice>("HeadDevice");
			Platform = joinRequest.Data.ExtractOrThrow<Platform>("Platform");
			DeviceInfos = joinRequest.Data.TryGetList("Devices");
			if (UserID != null)
			{
				banAccessKey = joinRequest.Data.ExtractOrThrow<string>("BanAccessKeyId");
			}
			HasEyeTracking = joinRequest.Data.ExtractOrThrow<bool>("EyeTracking");
			HasPupilTracking = joinRequest.Data.ExtractOrThrow<bool>("PupilTracking");
			MouthTrackingParameters = (from n in joinRequest.Data.TryGetList("MouthTracking")
				select ((DataTreeValue)n).ExtractEnum<MouthParameterGroup>()).ToList();
			KioskMode = joinRequest.Data.ExtractOrDefault("KioskMode", def: false);
			hostVerificationToken = Convert.FromBase64String(joinRequest.Data.ExtractOrThrow<string>("HostVerificationToken"));
			ExtraIDs = new Dictionary<string, string>();
			DataTreeDictionary extraIds = joinRequest.Data.TryGetDictionary("ExtraIds");
			if (extraIds != null)
			{
				foreach (KeyValuePair<string, DataTreeNode> extraId in extraIds.Children)
				{
					ExtraIDs.Add(extraId.Key, extraId.Value.LoadString());
				}
			}
		}
		catch (Exception ex)
		{
			throw new SessionRejectJoinException("World.Error.SecurityViolation", "Exception processing join request: " + ex.Message, tempBan: false, ex);
		}
		if (!IsUserDataValid())
		{
			BanManager.TempBanConnection(Connection);
			throw new SessionRejectJoinException("World.Error.SecurityViolation", "Invalid user data");
		}
		identityHash = CryptoHelper.GenerateCryptoBlob(64);
		if (UserID != null)
		{
			publicKeyTask = Cloud.Users.GetPublicKey(UserID, UserSessionID);
			userDataTask = Cloud.Users.GetUser(UserID, banAccessKey);
		}
	}

	private bool IsUserDataValid()
	{
		if (Username != null && Username.Length > 32)
		{
			return false;
		}
		if (!FrooxEngine.Store.LocalDB.IsValidMachineId(MachineID))
		{
			return false;
		}
		if (!HeadDevice.IsDefined())
		{
			return false;
		}
		if (!Platform.IsDefined())
		{
			return false;
		}
		if (UserID != null && string.IsNullOrWhiteSpace(banAccessKey))
		{
			return false;
		}
		return true;
	}

	private async Task SendJoinChallenge()
	{
		ControlMessage challenge = new ControlMessage(ControlMessage.Message.JoinChallenge);
		challenge.Targets.Add(Connection);
		challenge.Data.Add("IdentityHash", Convert.ToBase64String(identityHash));
		challenge.Data.Add("SessionID", World.SessionId);
		string machineVerificationSignature = Convert.ToBase64String(Crypto.SignMachineVerificationToken(hostVerificationToken, World.SessionId, host: true));
		challenge.Data.Add("HostMachineID", Engine.LocalDB.MachineID);
		challenge.Data.Add("MachinePublicKeyModulus", Convert.ToBase64String(Engine.LocalDB.PublicKeyModulus));
		challenge.Data.Add("MachinePublicKeyExponent", Convert.ToBase64String(Engine.LocalDB.PublicKeyExponent));
		challenge.Data.Add("MachineVerificationSignature", machineVerificationSignature);
		if (World.HostUser.UserID != null)
		{
			challenge.Data.Add("HostUserID", World.HostUser.UserID);
			challenge.Data.Add("HostUserSessionID", World.HostUser.UserSessionId);
			string cloudVerificationSignature = Convert.ToBase64String(Crypto.SignCloudVerificationToken(hostVerificationToken, World.SessionId, host: true));
			challenge.Data.Add("CloudVerificationSignature", cloudVerificationSignature);
			CloudResult<OneTimeVerificationKey> key = await Engine.Cloud.Security.CreateKey(null, VerificationKeyUse.AccessBans).ConfigureAwait(continueOnCapturedContext: false);
			if (!key.IsOK)
			{
				throw new SessionRejectJoinException("World.Error.FailedFetchingAuthentication", $"Failed to generate ban access key. Result: {key}");
			}
			challenge.Data.Add("BanAccessKeyId", key.Entity.KeyId);
		}
		challenge.Data.Add("SystemCompatibilityHash", GlobalTypeRegistry.SystemCompatibilityHash);
		DataTreeList assemblyList = new DataTreeList();
		foreach (AssemblyTypeRegistry assembly in World.Types.AllowedAssemblies)
		{
			if (!assembly.IsDependency)
			{
				DataTreeDictionary assemblyInfo = new DataTreeDictionary();
				assemblyInfo.Add("Name", assembly.AssemblyName);
				assemblyInfo.Add("CompatibilityHash", assembly.CompatibilityHash);
				assemblyList.Add(assemblyInfo);
			}
		}
		challenge.Data.Add("DataModelAssemblies", assemblyList);
		Messages.Outgoing.EnqueueForTransmission(challenge, isOrigin: true, useBackgroundEncoder: false);
	}

	private async Task VerifyJoinIdentity(ControlMessage authentication)
	{
		string machineIdentitySignatureStr = authentication.Data.ExtractOrDefault<string>("MachineIdentitySignature");
		if (machineIdentitySignatureStr != null)
		{
			machineIdentitySignature = Convert.FromBase64String(machineIdentitySignatureStr);
		}
		string cloudIdentitySignatureStr = authentication.Data.ExtractOrDefault<string>("CloudIdentitySignature");
		if (cloudIdentitySignatureStr != null)
		{
			cloudIdentitySignature = Convert.FromBase64String(cloudIdentitySignatureStr);
		}
		if (machineIdentitySignature == null || machineIdentitySignature.Length == 0)
		{
			throw new SessionRejectJoinException("World.Error.FailedMachineID", "MachineID verification data cannot be missing");
		}
		if (!FrooxEngine.Store.LocalDB.MachineIdMatches(MachineID, MachinePublicKey) || !Crypto.VerifyToken(identityHash, World.SessionId, host: false, machineIdentitySignature, MachinePublicKey))
		{
			throw new SessionRejectJoinException("World.Error.FailedAuthentication", "Failed to validate MachineID");
		}
		if (UserID != null)
		{
			CloudResult<RSAParametersData> publicKeyResult = await publicKeyTask;
			CloudResult<SkyFrost.Base.User> userResult = await userDataTask;
			if (userResult.Entity == null)
			{
				throw new SessionRejectJoinException("World.Error.FailedUserID", $"Failed to authenaticate UserId: {UserID}, UserSessionId: {UserSessionID}. KeyResult: {publicKeyResult}. UserResult: {userResult}");
			}
			CloudUser = userResult.Entity;
			if (CloudUser.Username != Username)
			{
				throw new SessionRejectJoinException("World.Error.SecurityViolation", "Username mismatch", tempBan: true);
			}
			if (cloudIdentitySignature == null || cloudIdentitySignature.Length == 0 || publicKeyResult.Entity == null)
			{
				throw new SessionRejectJoinException("World.Error.FailedUserID", "Failed to get cloud identity signature data");
			}
			RSAParametersData publicKey = publicKeyResult.Entity;
			if (publicKey.Modulus == null || publicKey.Exponent == null)
			{
				throw new SessionRejectJoinException("World.Error.FailedFetchingAuthentication", "Failed to fetch authentication data");
			}
			if (!Crypto.VerifyToken(identityHash, World.SessionId, host: false, cloudIdentitySignature, publicKey))
			{
				throw new SessionRejectJoinException("World.Error.FailedAuthentication", "Failed to verify user cloud identity");
			}
			IdentityVerified = true;
		}
	}

	private void RejectJoin(string reason)
	{
		if (!Connection.IsOpen)
		{
			return;
		}
		ControlMessage rejection = new ControlMessage(ControlMessage.Message.JoinReject);
		rejection.Targets.Add(Connection);
		rejection.Data.Add("Reason", reason);
		Messages.Outgoing.EnqueueForTransmission(rejection, isOrigin: true, useBackgroundEncoder: false);
		Task.Run(async delegate
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(5L)).ConfigureAwait(continueOnCapturedContext: false);
				if (Connection.IsOpen)
				{
					UniLog.Warning($"Connection {Connection} is still open after rejecting join, closing forcibly");
					Connection.Close();
				}
			}
			catch (Exception value)
			{
				UniLog.Error($"Exception forcibly closing connection after rejecting join:\n{value}");
			}
		});
	}

	private void GrantJoinAndCreateUser()
	{
		ControlMessage grant = new ControlMessage(ControlMessage.Message.JoinGrant);
		grant.Targets.Add(Connection);
		User = World.CreateGuestUser();
		User.InitializingEnabled = true;
		User.MachineID = MachineID;
		User.UserID = UserID;
		User.UserSessionId = UserSessionID;
		User.UserName = Username ?? ("Guest " + User.AllocationID);
		User.HeadDevice = HeadDevice;
		User.Platform = Platform;
		User.SetupDeviceInfos(DeviceInfos);
		User.SetupEyeTracking(HasEyeTracking, HasPupilTracking);
		User.SetupMouthTracking(MouthTrackingParameters);
		User.SetupExtraIds(ExtraIDs);
		User.KioskMode = KioskMode;
		if (SpectatorBan)
		{
			User.DefaultSpectator = true;
		}
		if (MuteBan)
		{
			User.DefaultMute = true;
		}
		if (World.Permissions.IsSilenced(User))
		{
			User.IsSilenced = true;
		}
		if (RoleOverride != null)
		{
			PermissionSet role = World.Permissions.FindRoleByName(RoleOverride);
			PermissionSet permissionSet = World.Permissions.FilterRole(role);
			if (permissionSet != role)
			{
				UniLog.Log($"Cannot use role override {RoleOverride} for {UserID}, because it's higher than the host role.");
			}
			if (permissionSet != null)
			{
				User.Role = role;
			}
		}
		if (User.Role == null)
		{
			User.Role = World.Permissions.GetDefaultRole(User);
		}
		User.InitializingEnabled = false;
		Connections.LinkConnectionToUser(this);
		grant.Data.Add("ReferenceID", (ulong)User.ReferenceID);
		grant.Data.Add("RandomizationSeed", World.RandomizationSeed);
		grant.Data.Add("OBFK", Convert.ToBase64String(World.Obfuscation_KEY));
		grant.Data.Add("OBFI", Convert.ToBase64String(World.Obfuscation_IV));
		Messages.Outgoing.EnqueueForTransmission(grant, isOrigin: true, useBackgroundEncoder: false);
		Sync.ScheduleUserToInitialize(User);
	}

	public void Dispose()
	{
		Connection.Close();
	}
}
