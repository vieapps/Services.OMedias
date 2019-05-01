#region Related components
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.OMedias
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Contents", TableName = "T_OMedias_Contents", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class Content : Repository<Content>
	{
		public Content() : base() { }

		[Property(MaxLength = 250, NotEmpty = true), Sortable, Searchable, FormControl(AutoFocus = true, Label = "{{omedias.controls.[name].label}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 250), Searchable, FormControl(ControlType = "TextArea", Label = "{{omedias.controls.[name].label}}")]
		public string Summary { get; set; } = "";

		[Property(MaxLength = 250), Sortable(IndexName = "Info"), Searchable, FormControl(Label = "{{omedias.controls.[name].label}}")]
		public string Speakers { get; set; } = "";

		[Property(MaxLength = 2000), FormControl(DataType = "url", Label = "{{omedias.controls.[name].label}}", Description = "{{omedias.controls.[name].description}}")]
		public string MediaURI { get; set; } = "";

		[Property(MaxLength = 250, NotEmpty = true), Sortable(IndexName = "Categories"), FormControl(ControlType = "Select", SelectValuesRemoteURI = "discovery/definitions?x-service-name=omedias&x-object-name=categories", SelectAsBoxes = true, Multiple = true, Label = "{{omedias.controls.[name].label}}")]
		public string Categories { get; set; } = "";

		[Property(MaxLength = 250), FormControl(Label = "{{omedias.controls.[name].label}}", Description = "{{omedias.controls.[name].description}}")]
		public string Tags { get; set; } = "";

		[Property(NotNull = true), Sortable(IndexName = "Times"), FormControl(ControlType = "DatePicker", DatePickerWithTimes = true, Label = "{{omedias.controls.[name].label}}", Description = "{{omedias.controls.[name].description}}")]
		public DateTime StartingTime { get; set; } = DateTime.Now;

		[Property(MaxLength = 19), Sortable(IndexName = "Times"), FormControl(ControlType = "DatePicker", DataType = "date", DatePickerWithTimes = true, Label = "{{omedias.controls.[name].label}}", Description = "{{omedias.controls.[name].description}}")]
		public string EndingTime { get; set; } = "-";

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Statistics"), FormControl(Label = "{{omedias.controls.[name].label}}", Description = "{{omedias.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Draft;

		[Property(IsCLOB = true), FormControl(Label = "{{omedias.controls.[name].label}}", Excluded = true)]
		public string Details { get; set; }

		[Property(MaxLength = 32, NotEmpty = false, NotNull = false), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public string ParentID { get; set; }

		Content _parent = null;
		[Ignore, BsonIgnore, JsonIgnore, FormControl(Excluded = true)]
		public new Content Parent
			=> string.IsNullOrWhiteSpace(this.ParentID) ? null : this._parent ?? (this._parent = Content.Get<Content>(this.ParentID));

		[Property(MaxLength = 4), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public string OrderIndex { get; set; }

		[Sortable(IndexName = "Times"), FormControl(Excluded = true)]
		public DateTime LastUpdated { get; set; } = DateTime.Now;

		[Ignore, BsonIgnore, FormControl(Excluded = true)]
		public string MediaType
		{
			get
			{
				var uri = this.MediaURI ?? "";
				return uri.IsEndsWith(".mp3")
					? "Audio"
					: uri.IsEndsWith(".mp4")
						? "Video"
						: "Text";
			}
		}

		[Ignore, BsonIgnore, FormControl(Excluded = true)]
		public List<string> Images { get; set; } = new List<string>();

		[AsJson, FormControl(Excluded = true)]
		public List<CounterInfo> Counters { get; set; } = new List<CounterInfo>
		{
			new CounterInfo { Type = "View" },
			new CounterInfo { Type = "Download" }
		};

		[Sortable(IndexName = "Management"), FormControl(Excluded = true)]
		public DateTime Created { get; set; } = DateTime.Now;

		[Property(MaxLength = 32), Sortable(IndexName = "Management"), FormControl(Excluded = true)]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Management"), FormControl(Excluded = true)]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Property(MaxLength = 32), Sortable(IndexName = "Management"), FormControl(Excluded = true)]
		public string LastModifiedID { get; set; } = "";

		#region To JSON
		public JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null, bool asNormalized = true)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (asNormalized)
				{
					var download = this.Counters.FirstOrDefault(c => c.Type.IsEquals("Download"));
					if (download != null)
					{
						var gotUpdated = false;

						if (!download.LastUpdated.IsInCurrentMonth() && download.Total == download.Month)
						{
							download.Month = 0;
							gotUpdated = true;
						}

						if (!download.LastUpdated.IsInCurrentWeek() && download.Total == download.Week)
						{
							download.Week = 0;
							gotUpdated = true;
						}

						if (gotUpdated)
							json["Counters"] = this.Counters.ToJArray();
					}

					json["EndingTime"] = this.EndingTime.Equals("-") ? null : DateTime.Parse(this.EndingTime).ToIsoString();
				}
				onPreCompleted?.Invoke(json);
			});

		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted)
			=> this.ToJson(addTypeOfExtendedProperties, onPreCompleted, true);
		#endregion

	}
}