#region Related components
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Dynamic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.OMedias
{
	public class ServiceComponent : ServiceBase
	{
		public override string ServiceName => "OMedias";

		public override void Start(string[] args = null, bool initializeRepository = true, Func<IService, Task> nextAsync = null)
		{
			// initialize caching storage
			Utility.Cache = new Components.Caching.Cache($"VIEApps-Services-{this.ServiceName}", Components.Utility.Logger.GetLoggerFactory());

			// start the service
			base.Start(args, initializeRepository, nextAsync);
		}

		public override async Task<JToken> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default(CancellationToken))
		{
			var stopwatch = Stopwatch.StartNew();
			this.WriteLogs(requestInfo, $"Begin request ({requestInfo.Verb} {requestInfo.GetURI()})");
			try
			{
				JToken json = null;
				switch (requestInfo.ObjectName.ToLower())
				{
					case "content":
						json = await this.ProcessContentAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "profile":
						json = await this.ProcessProfileAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "definitions":
						var objectIdentity = (requestInfo.GetObjectIdentity() ?? "").ToLower();
						switch (objectIdentity)
						{
							case "categories":
							case "groups":
							case "lists":
								json = JArray.Parse((await UtilityService.ReadTextFileAsync(Path.Combine(this.GetPath("StaticFiles"), this.ServiceName.ToLower(), $"{objectIdentity.ToLower()}.json"), null, cancellationToken).ConfigureAwait(false)).Replace("\r", "").Replace("\t", ""));
								break;

							case "content":
								if (!requestInfo.Query.TryGetValue("mode", out string mode) || "forms".IsEquals(mode))
									json = this.GenerateFormControls<Content>();
								else
									throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
								break;

							default:
								throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
						}
						break;

					default:
						throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
				}
				stopwatch.Stop();
				this.WriteLogs(requestInfo, $"Success response - Execution times: {stopwatch.GetElapsedTimes()}");
				if (this.IsDebugResultsEnabled)
					this.WriteLogs(requestInfo,
						$"- Request: {requestInfo.ToJson().ToString(this.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}" + "\r\n" +
						$"- Response: {json?.ToString(this.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}"
					);
				return json;
			}
			catch (Exception ex)
			{
				throw this.GetRuntimeException(requestInfo, ex, stopwatch);
			}
		}

		protected override List<Privilege> GetPrivileges(IUser user, Privileges privileges)
			=> "content,profile".ToList()
				.Select(objName => new Privilege(this.ServiceName, objName, null, this.GetPrivilegeRole(user)))
				.ToList();

		Task<JObject> ProcessContentAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? this.SearchContentsAsync(requestInfo, cancellationToken)
						: this.GetContentAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateContentAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateContentAsync(requestInfo, cancellationToken);

				case "DELETE":
					return this.DeleteContentAsync(requestInfo, cancellationToken);

				default:
					return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
			}
		}

		#region Search contents
		DateTime GetNowHourQuater()
		{
			var minute = DateTime.Now.Minute >= 45
				? 45
				: DateTime.Now.Minute >= 30
					? 30
					: DateTime.Now.Minute >= 15
						? 15
						: 0;
			return new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, minute, 0, 0);
		}

		async Task<JObject> SearchContentsAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			if (!await this.IsAuthorizedAsync(
					requestInfo,
					Components.Security.Action.View,
					null,
					(user, privileges) => this.GetPrivileges(user, privileges),
					(role) => this.GetPrivilegeActions(role)
				).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			if (request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Content>() is FilterBys<Content> filter)
			{
				if (filter.Children.FirstOrDefault(f => f is FilterBy<Content> && (f as FilterBy<Content>).Attribute.IsEquals("Status")) == null && !await this.IsServiceAdministratorAsync(requestInfo).ConfigureAwait(false))
					filter.Add(Filters<Content>.Equals("Status", $"{ApprovalStatus.Published}"));
			}
			else
				filter = Filters<Content>.And(
					Filters<Content>.Equals("Status", $"{ApprovalStatus.Published}"),
					Filters<Content>.GreaterOrEquals("StartingTime", "@nowHourQuater()"),
					Filters<Content>.Or(
						Filters<Content>.Equals("EndingTime", "-"),
						Filters<Content>.LessThan("EndingTime", "@nowHourQuater()")
					),
					Filters<Content>.IsNull("ParentID")
				);

			var filterBy = filter.Clone();
			if (filterBy.Children.FirstOrDefault(f => f is FilterBy<Content> && (f as FilterBy<Content>).Attribute.IsEquals("StartingTime")) is FilterBy<Content> startingTime && "@nowHourQuater()".IsStartsWith(startingTime.Value as string))
				startingTime.Value = this.GetNowHourQuater();
			if (filterBy.Children.FirstOrDefault(f => f is FilterBy<Content> && (f as FilterBy<Content>).Attribute.IsEquals("EndingTime")) is FilterBy<Content> endingTime && "@nowHourQuater()".IsStartsWith(endingTime.Value as string))
				endingTime.Value = this.GetNowHourQuater().ToDTString();
			if (filterBy.Children.FirstOrDefault(f => f is FilterBys<Content> && (f as FilterBys<Content>).Operator.Equals(GroupOperator.Or) && (f as FilterBys<Content>).Children.FirstOrDefault(cf => cf is FilterBy<Content> && (cf as FilterBy<Content>).Attribute.IsEquals("EndingTime")) != null) is FilterBys<Content> orEndingTime)
				orEndingTime.Children.Where(f => f is FilterBy<Content> && (f as FilterBy<Content>).Attribute.IsEquals("EndingTime") && "@nowHourQuater()".IsStartsWith((f as FilterBy<Content>).Value as string)).ForEach(f => (f as FilterBy<Content>).Value = this.GetNowHourQuater().ToDTString());

			Func<string> func_GetNowHourQuater = () => this.GetNowHourQuater().ToDTString();
			filterBy.Prepare(null, requestInfo, new Dictionary<string, object>
			{
				{ "nowHourQuater", func_GetNowHourQuater }
			});

			var sortBy = request.Get<ExpandoObject>("SortBy", null)?.ToSortBy<Content>();
			if (sortBy == null && string.IsNullOrWhiteSpace(query))
			{
				var filterByParentID = filterBy.Children.FirstOrDefault(f => f is FilterBy<Content> && (f as FilterBy<Content>).Attribute.IsEquals("ParentID"));
				sortBy = filterByParentID != null && filterByParentID is FilterBy<Content> && (filterByParentID as FilterBy<Content>).Value != null && (filterByParentID as FilterBy<Content>).Value.ToString().IsValidUUID()
					? Sorts<Content>.Descending("OrderIndex")
					: Sorts<Content>.Descending("StartingTime").ThenByDescending("LastUpdated");
			}

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageNumber = pagination.Item4;

			// check cache
			var cacheKey = string.IsNullOrWhiteSpace(query)
				? this.GetCacheKey(filterBy, sortBy)
				: "";

			var json = !cacheKey.Equals("")
				? await Utility.Cache.GetAsync<string>($"{cacheKey }{pageNumber}:json").ConfigureAwait(false)
				: "";

			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1
				? pagination.Item1
				: string.IsNullOrWhiteSpace(query)
					? await Content.CountAsync(filterBy, $"{cacheKey}total", cancellationToken).ConfigureAwait(false)
					: await Content.CountAsync(query, filterBy, cancellationToken).ConfigureAwait(false);

			var pageSize = pagination.Item3;

			var totalPages = (new Tuple<long, int>(totalRecords, pageSize)).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Content.FindAsync(filterBy, sortBy, pageSize, pageNumber, $"{cacheKey}{pageNumber}", cancellationToken).ConfigureAwait(false)
					: await Content.SearchAsync(query, filterBy, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Content>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var result = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sortBy?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.ToJsonArray() }
			};

			// update cache
			if (!cacheKey.Equals(""))
				await Utility.Cache.SetAsync($"{cacheKey }{pageNumber}:json", result.ToString(this.IsDebugLogEnabled ? Formatting.Indented : Formatting.None), Utility.Cache.ExpirationTime / 2, cancellationToken).ConfigureAwait(false);

			// return the result
			return result;
		}

		async Task SendLastUpdatedContentsAsync()
		{
			try
			{
				var filterBy = Filters<Content>.And(
					Filters<Content>.Equals("Status", $"{ApprovalStatus.Published}"),
					Filters<Content>.GreaterOrEquals("StartingTime", "@nowHourQuater()"),
					Filters<Content>.Or(
						Filters<Content>.Equals("EndingTime", "-"),
						Filters<Content>.LessThan("EndingTime", "@nowHourQuater()")
					),
					Filters<Content>.IsNull("ParentID")
				);
				Func<string> func_GetNowHourQuater = () => this.GetNowHourQuater().ToDTString();
				filterBy.Prepare(null, null, new Dictionary<string, object>
				{
					{ "nowHourQuater", func_GetNowHourQuater }
				});
				var sortBy = Sorts<Content>.Descending("StartingTime").ThenByDescending("LastUpdated");
				var contents = await Content.FindAsync(filterBy, sortBy, 20, 1, $"{this.GetCacheKey(filterBy, sortBy)}:1", this.CancellationTokenSource.Token).ConfigureAwait(false);
				await this.SendUpdateMessagesAsync(
					contents.Select(book => new BaseMessage
					{
						Type = $"{this.ServiceName}#Content#Update",
						Data = book.ToJson()
					}).ToList(),
					"*",
					null,
					this.CancellationTokenSource.Token
				).ConfigureAwait(false);
			}
			catch { }
		}
		#endregion

		#region Create a content
		async Task<JObject> CreateContentAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permission on convert
			if (requestInfo.Extra != null && requestInfo.Extra.ContainsKey("x-convert"))
			{
				if (!await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false))
					throw new AccessDeniedException();
			}

			// check permission on create new
			else if (!await this.IsAuthorizedAsync(
				requestInfo,
				Components.Security.Action.Update,
				null,
				(user, privileges) => this.GetPrivileges(user, privileges),
				(role) => this.GetPrivilegeActions(role)
			).ConfigureAwait(false))
				throw new AccessDeniedException();

			// update information
			var body = requestInfo.GetBodyExpando();
			var content = body.Copy<Content>("ID,LastUpdated,LastModified,LastModifiedID,Created,CreatedID,EndingTime,Images,Counters".ToHashSet());
			content.ID = UtilityService.NewUUID;
			content.Created = content.LastModified = content.LastUpdated = DateTime.Now;
			content.CreatedID = content.LastModifiedID = requestInfo.Session.User.ID;

			var endingTime = body.Get<string>("EndingTime");
			content.EndingTime = endingTime != null ? DateTime.Parse(endingTime).ToDTString() : "-";
			if (string.IsNullOrWhiteSpace(content.ParentID))
				content.ParentID = content.OrderIndex = null;

			// update database
			await Content.CreateAsync(content, cancellationToken).ConfigureAwait(false);

			// send update message
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#Content#Update",
				DeviceID = "*",
				Data = content.ToJson()
			}, cancellationToken).ConfigureAwait(false);

			// return
			return content.ToJson();
		}
		#endregion

		#region Get a content & update related (counters, ...)
		async Task<JObject> GetContentAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// get the book
			var objectIdentity = requestInfo.GetObjectIdentity();

			var id = !string.IsNullOrWhiteSpace(objectIdentity) && objectIdentity.IsValidUUID()
				? objectIdentity
				: requestInfo.Query.ContainsKey("id")
					? requestInfo.Query["id"]
					: null;

			var content = await Content.GetAsync<Content>(id, cancellationToken).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException();

			// counters
			if ("counters".IsEquals(objectIdentity))
			{
				// update counters
				var result = await this.UpdateCounterAsync(content, requestInfo.Query.ContainsKey("action") ? requestInfo.Query["action"] : "View", cancellationToken).ConfigureAwait(false);

				// send update message
				await this.SendUpdateMessageAsync(new UpdateMessage
				{
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Type = $"{this.ServiceName}#Content#Counters",
					Data = result
				}, cancellationToken).ConfigureAwait(false);

				// return update
				return result;
			}

			// detail information
			else
				return content.ToJson();
		}

		async Task<JObject> UpdateCounterAsync(Content content, string action, CancellationToken cancellationToken = default(CancellationToken))
		{
			// get and update
			var counter = content.Counters.FirstOrDefault(c => c.Type.IsEquals(action));
			if (counter != null)
			{
				// update counters
				counter.Total++;
				counter.Week = counter.LastUpdated.IsInCurrentWeek() ? counter.Week + 1 : 1;
				counter.Month = counter.LastUpdated.IsInCurrentMonth() ? counter.Month + 1 : 1;
				counter.LastUpdated = DateTime.Now;

				// reset counter of download
				if (!"download".IsEquals(action))
				{
					var downloadCounter = content.Counters.FirstOrDefault(c => c.Type.IsEquals("download"));
					if (downloadCounter != null)
					{
						if (!downloadCounter.LastUpdated.IsInCurrentWeek())
							downloadCounter.Week = 0;
						if (!downloadCounter.LastUpdated.IsInCurrentMonth())
							downloadCounter.Month = 0;
						downloadCounter.LastUpdated = DateTime.Now;
					}
				}

				// update database
				await Content.UpdateAsync(content, true, cancellationToken).ConfigureAwait(false);
			}

			// return data
			return new JObject
			{
				{ "ID", content.ID },
				{ "Counters", content.Counters.ToJArray(c => c.ToJson()) }
			};
		}
		#endregion

		#region Update a content
		async Task<JObject> UpdateContentAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			if (!await this.IsAuthorizedAsync(
				requestInfo,
				Components.Security.Action.Update,
				null,
				(user, privileges) => this.GetPrivileges(user, privileges),
				(role) => this.GetPrivilegeActions(role)
			).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var content = await Content.GetAsync<Content>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException();

			// update information
			var body = requestInfo.GetBodyExpando();
			content.CopyFrom(body, "ID,LastUpdated,LastModified,LastModifiedID,Created,CreatedID,EndingTime,Images,Counters".ToHashSet());
			content.LastModified = content.LastUpdated = DateTime.Now;
			content.LastModifiedID = requestInfo.Session.User.ID;

			var endingTime = body.Get<string>("EndingTime");
			content.EndingTime = endingTime != null ? DateTime.Parse(endingTime).ToDTString() : "-";

			// update database
			await Content.UpdateAsync(content, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// send update message
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#Content#Update",
				DeviceID = "*",
				Data = content.ToJson()
			}, cancellationToken).ConfigureAwait(false);

			// return
			return content.ToJson();
		}
		#endregion

		#region Delete a content
		async Task<JObject> DeleteContentAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			if (!await this.IsAuthorizedAsync(
				requestInfo,
				Components.Security.Action.Delete,
				null,
				(user, privileges) => this.GetPrivileges(user, privileges),
				(role) => this.GetPrivilegeActions(role)
			).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var content = await Content.GetAsync<Content>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException();

			// delete from database
			await Content.DeleteAsync<Content>(content.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// send update message
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#Content#Delete",
				DeviceID = "*",
				Data = new JObject
				{
					{ "ID", content.ID },
					{ "Categories", content.Categories }
				}
			}, cancellationToken).ConfigureAwait(false);

			// return
			return new JObject();
		}
		#endregion

		Task<JObject> ProcessProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return this.GetProfileAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateProfileAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateProfileAsync(requestInfo, cancellationToken);

				default:
					return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
			}
		}

		#region Create an account profile
		async Task<JObject> CreateProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare identity
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (requestInfo.Extra != null && requestInfo.Extra.ContainsKey("x-convert"))
			{
				if (!isSystemAdministrator)
					throw new AccessDeniedException();
			}

			// check permission on create
			else
			{
				var gotRights = isSystemAdministrator || (this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id));
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// create account profile
			var account = requestInfo.GetBodyJson().Copy<Account>();

			// reassign identity
			if (requestInfo.Extra == null || !requestInfo.Extra.ContainsKey("x-convert"))
				account.ID = id;

			// update database
			await Account.CreateAsync(account, cancellationToken).ConfigureAwait(false);
			return account.ToJson();
		}
		#endregion

		#region Get an account profile
		async Task<JObject> GetProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;
			var gotRights = this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id);
			if (!gotRights)
				gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				gotRights = await this.IsAuthorizedAsync(requestInfo, Components.Security.Action.View, null, this.GetPrivileges, this.GetPrivilegeActions).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// get information
			var account = await Account.GetAsync<Account>(id, cancellationToken).ConfigureAwait(false);

			// special: not found
			if (account == null)
			{
				if (id.Equals(requestInfo.Session.User.ID))
				{
					account = new Account
					{
						ID = id
					};
					await Account.CreateAsync(account).ConfigureAwait(false);
				}
				else
					throw new InformationNotFoundException();
			}

			// return JSON
			return account.ToJson();
		}
		#endregion

		#region Update an account profile
		async Task<JObject> UpdateProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;
			var gotRights = this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id);
			if (!gotRights)
				gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				gotRights = await this.IsAuthorizedAsync(requestInfo, Components.Security.Action.Update, null, this.GetPrivileges, this.GetPrivilegeActions).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// get existing information
			var account = await Account.GetAsync<Account>(id, cancellationToken).ConfigureAwait(false);
			if (account == null)
				throw new InformationNotFoundException();

			// update
			account.CopyFrom(requestInfo.GetBodyJson(), "ID,Title".ToHashSet(), _ => account.Title = null);
			await Account.UpdateAsync(account, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			return account.ToJson();
		}
		#endregion

		#region Process inter-communicate messages
		protected override async Task ProcessInterCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default(CancellationToken))
		{
			// prepare
			var data = message.Data?.ToExpandoObject();
			if (data == null)
				return;

			// update counters
			if (message.Type.IsEquals("Download") && !string.IsNullOrWhiteSpace(data.Get<string>("UserID")) && !string.IsNullOrWhiteSpace(data.Get<string>("ContentID")))
				try
				{
					var content = await Content.GetAsync<Content>(data.Get<string>("ContentID"), cancellationToken).ConfigureAwait(false);
					if (content != null)
					{
						var result = await this.UpdateCounterAsync(content, Components.Security.Action.Download.ToString(), cancellationToken).ConfigureAwait(false);
						await this.SendUpdateMessageAsync(new UpdateMessage
						{
							DeviceID = "*",
							Type = $"{this.ServiceName}#Content#Counters",
							Data = result
						}, cancellationToken).ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					await this.WriteLogsAsync(UtilityService.NewUUID, "Error occurred while updating counters", ex).ConfigureAwait(false);
				}
		}
		#endregion

	}
}