using System.Diagnostics;
using OpenTelemetry;
using Ruya.Primitives;

namespace Ruya.Observability.Processors;

internal class EnvironmentTagProcessor : BaseProcessor<Activity>
{
	public override void OnStart(Activity data)
	{
		data.AddTag("environment", EnvironmentHelper.EnvironmentName);
	}
}
