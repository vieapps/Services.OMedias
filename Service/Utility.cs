#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.OMedias
{
	public static class Utility
	{
		public static Components.Caching.Cache Cache { get; internal set; }

		internal static JObject UpdateFiles(this JObject json, JToken files)
		{
			if (files != null)
			{
				var thumbnails = files.Get<JArray>("Thumbnails");
				if (thumbnails != null)
					json["Thumbnails"] = thumbnails;
				var attachments = files.Get<JArray>("Attachments");
				if (attachments != null)
					json["Attachments"] = attachments;
				var images = new JArray();
				thumbnails?.ForEach(thumbnail => images.Add(thumbnail["URI"]));
				attachments?.Where(attachment => (attachment.Get<string>("ContentType") ?? "").IsStartsWith("image/")).ForEach(attachment => images.Add(attachment["URIs"]["Direct"]));
				json["Images"] = images;
			}
			return json;
		}
	}

	//  --------------------------------------------------------------------------------------------

	[Serializable, Repository]
	public abstract class Repository<T> : RepositoryBase<T> where T : class
	{
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string ServiceName => ServiceBase.ServiceComponent.ServiceName;
	}
}