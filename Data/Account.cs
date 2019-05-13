#region Related components
using System;
using System.Diagnostics;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.OMedias
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}")]
	[Entity(CollectionName = "Accounts", TableName = "T_OMedias_Accounts", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class Account : Repository<Account>
	{
		public Account() : base() { }

		[JsonIgnore, AsJson]
		public List<string> Favorites { get; set; } = new List<string>();

		[JsonIgnore, BsonIgnore, Ignore]
		public override string Title { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string SystemID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override Privileges OriginalPrivileges { get; set; }
	}
}