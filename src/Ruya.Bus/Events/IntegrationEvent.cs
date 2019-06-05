using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ruya.Bus.Events
{
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

		[JsonProperty]
		public Guid Id { get; private set; }

		[JsonProperty]
		public DateTime CreationDate { get; private set; }

		[JsonProperty]
		public object Data { get; set; }

		[JsonIgnore]
		public bool PublishAsError { get; set; }

		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public List<object> Error { get; set; }
	}
}
