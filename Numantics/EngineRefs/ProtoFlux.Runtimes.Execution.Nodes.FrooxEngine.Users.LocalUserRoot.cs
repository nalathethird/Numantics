// ProtoFlux.Nodes.FrooxEngine, Version=2025.9.23.1237, Culture=neutral, PublicKeyToken=null
// ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Users.LocalUserRoot
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

[NodeCategory("Users")]
[ContinuouslyChanging]
public class LocalUserRoot : ObjectFunctionNode<FrooxEngineContext, UserRoot>
{
	protected override UserRoot Compute(FrooxEngineContext context)
	{
		return context.World.LocalUser?.Root;
	}
}
