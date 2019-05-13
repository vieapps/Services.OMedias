#region Related components
using System;
using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.OMedias
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("Type = {Type}, Total = {Total}")]
	public class CounterInfo
	{
		public CounterInfo() { }

		public string Type { get; set; } = "View";

		public int Total { get; set; } = 0;

		public DateTime LastUpdated { get; set; } = DateTime.Now;

		public int Month { get; set; } = 0;

		public int Week { get; set; } = 0;

		public new JObject ToJson(bool removeType = false)
		{
			var json = this.ToJson(null) as JObject;
			if (removeType)
				json.Remove("Type");
			return json;
		}
	}
}