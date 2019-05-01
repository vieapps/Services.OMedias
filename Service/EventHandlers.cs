#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.OMedias
{
	[EventHandlers]
	public class EventHandlers: IPostUpdateHandler, IPostDeleteHandler
	{
		public void OnPostUpdate<T>(RepositoryContext context, T @object, HashSet<string> changed, bool isRollback) where T : class
		{
			if (@object is Content)
			{

			}
		}

		public Task OnPostUpdateAsync<T>(RepositoryContext context, T @object, HashSet<string> changed, bool isRollback, CancellationToken cancellationToken) where T : class
		{
			if (@object is Content)
			{

			}
			return Task.CompletedTask;
		}

		public void OnPostDelete<T>(RepositoryContext context, T @object) where T : class
		{
			if (@object is Content)
			{

			}
		}

		public Task OnPostDeleteAsync<T>(RepositoryContext context, T @object, CancellationToken cancellationToken) where T : class
		{
			if (@object is Content)
			{
				
			}
			return Task.CompletedTask;
		}
	}
}