// FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// FrooxEngine.Session
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;

public class Session : IDisposable
{
	public const int CHALLENGE_KEY_TOKEN_LENGTH = 64;

	public TimeSpan Latency;

	private SpinQueue<IConnection> closedUserConnections = new SpinQueue<IConnection>();

	public bool IsDisposed { get; private set; }

	public World World { get; private set; }

	public Engine Engine => World.Engine;

	public NetworkManager NetworkManager => Engine.NetworkManager;

	public SessionAssetTransferer Assets { get; private set; }

	public SessionConnectionManager Connections { get; private set; }

	public SessionMessageManager Messages { get; private set; }

	public SessionSyncManager Sync { get; private set; }

	public SessionCryptoHelper Crypto { get; private set; }

	public static Session NewSession(World owner, ushort port = 0)
	{
		Session session = new Session(owner);
		session.StartNew(port);
		return session;
	}

	public static Session JoinSession(World owner, IEnumerable<Uri> addresses)
	{
		Session session = new Session(owner);
		session.ConnectTo(addresses);
		return session;
	}

	public void Dispose()
	{
		if (!IsDisposed)
		{
			GC.SuppressFinalize(this);
			IsDisposed = true;
			try
			{
				Messages.Dispose();
			}
			catch (Exception value)
			{
				UniLog.Error($"Exception disposing Messages:\n{value}");
			}
			try
			{
				Sync.Dispose();
			}
			catch (Exception value2)
			{
				UniLog.Error($"Exception disposing Sync:\n{value2}");
			}
			try
			{
				Connections.Dispose();
			}
			catch (Exception value3)
			{
				UniLog.Error($"Exception disposing Connections:\n{value3}");
			}
			try
			{
				Assets.Dispose();
			}
			catch (Exception value4)
			{
				UniLog.Error($"Exception disposing Assets:\n{value4}");
			}
			World = null;
		}
	}

	private Session(World owner)
	{
		World = owner;
		Assets = new SessionAssetTransferer(this);
		Connections = new SessionConnectionManager(this);
		Messages = new SessionMessageManager(this);
		Sync = new SessionSyncManager(this);
		Crypto = new SessionCryptoHelper(this);
	}

	private void StartNew(ushort port)
	{
		World.NetworkInitStart();
		if (!Connections.StartListener(port))
		{
			World.InitializationFailed(FrooxEngine.World.FailReason.NetworkError, "Network Error");
			return;
		}
		World.CreateHostUser();
		World.StartRunning();
		Sync.StartSyncLoop(isHost: true);
	}

	private void ConnectTo(IEnumerable<Uri> addresses)
	{
		World.NetworkInitStart();
		Task.Run(async delegate
		{
			try
			{
				if (!(await Connections.ConnectToAsync(addresses)))
				{
					World.InitializationFailed(FrooxEngine.World.FailReason.NetworkError, "World.Error.FailedToConnect");
				}
			}
			catch (SessionJoinException ex)
			{
				UniLog.Warning($"Session join has failed. Reason: {ex.FailReason} - {ex.StatusMessage}\n{ex.Message}");
				World.InitializationFailed(ex.FailReason, ex.StatusMessage);
			}
			catch (Exception value)
			{
				UniLog.Error($"Exception when connecting to session:\n{value}");
				World.InitializationFailed(FrooxEngine.World.FailReason.UnhandledError, "World.Error.Unknown");
			}
		});
	}
}
