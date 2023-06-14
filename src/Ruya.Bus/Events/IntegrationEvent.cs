using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ruya.Bus.Events;

public class IntegrationEvent
{
	public IntegrationEvent()
	{
		Id = Guid.NewGuid();
		CreationDate = DateTime.UtcNow;
	}

	[JsonConstructor]
	public IntegrationEvent(Guid id, DateTime createDate)
	{
		Id = id;
		CreationDate = createDate;
	}

	[JsonPropertyName("id")] public Guid Id { get; private set; }

	[JsonPropertyName("creationDate")] public DateTime CreationDate { get; private set; }

	[JsonPropertyName("data")] public object Data { get; set; }

	[JsonIgnore] public bool PublishAsError { get; set; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<object> Error { get; set; }
}
