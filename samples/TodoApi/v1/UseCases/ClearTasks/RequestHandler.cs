using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetCoreKit.Domain;
using NetCoreKit.Infrastructure.AspNetCore.CleanArch;
using NetCoreKit.Infrastructure.EfCore.Extensions;
using NetCoreKit.Samples.TodoAPI.Infrastructure.Db;

namespace NetCoreKit.Samples.TodoAPI.v1.UseCases.ClearTasks
{
    public class RequestHandler : TxRequestHandlerBase<ClearTasksRequest, ClearTasksResponse>
    {
        public RequestHandler(IUnitOfWorkAsync uow, IQueryRepositoryFactory queryRepositoryFactory)
            : base(uow, queryRepositoryFactory)
        {
        }

        public override async Task<ClearTasksResponse> Handle(ClearTasksRequest request,
            CancellationToken cancellationToken)
        {
            var projectRepository = CommandFactory.RepositoryAsync<Domain.Project>();
            var queryRepository = QueryFactory.QueryRepository<Domain.Project>();

            var project =
                await queryRepository.GetByIdAsync<TodoListDbContext, Domain.Project>(request.ProjectId, q => q.Include(x => x.Tasks), false);
            if (project == null)
                throw new Exception($"Couldn't found the project#{request.ProjectId}.");

            project.ClearTasks();
            await projectRepository.UpdateAsync(project);

            return new ClearTasksResponse();
        }
    }
}
