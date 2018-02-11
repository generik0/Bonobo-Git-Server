using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Bonobo.Git.Server.Data;
using Bonobo.Git.Server.Models;
using Bonobo.Git.Server.Security;
using Nancy;
using Newtonsoft.Json;

namespace Bonobo.Git.Server.Modules
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class SecurityModule : NancyModule
    {
        private readonly IMembershipService MembershipService;
        private readonly IRoleProvider RoleProvider;
        private readonly ITeamRepository TeamRepository;
        private readonly ITokenizer Tokenizer;

        public SecurityModule(IMembershipService membershipService, IRoleProvider roleProvider, ITeamRepository teamRepository, ITokenizer tokenizer)
        {
            MembershipService = membershipService;
            RoleProvider = roleProvider;
            TeamRepository = teamRepository;
            Tokenizer = tokenizer;
            Post["Security/login"] = _ => Login();
            Post["Security/Authorize"] = _ => IsAuthorized();
        }

        private Response IsAuthorized()
        {
            try
            {
                var token = Request.Query?.Token?.ToString();
                if(string.IsNullOrWhiteSpace(token))
                {
                    Response.AsJson(new LoginResultModel {Exception = "No token provided"}, HttpStatusCode.Forbidden);
                }
                var actual = Tokenizer.Decode(token);
                return actual ? HttpStatusCode.Accepted : HttpStatusCode.Forbidden;

            }
            catch (Exception exception)
            {
                return Response.AsJson(new LoginResultModel { Exception = exception.Message }, HttpStatusCode.Forbidden);
            }
        }

        private  Response Login()
        {
            try
            {
                //e.g. http://localhost:8080/Bonobo.Git.Server/api/security/login?Tor=admin&Freja=admin
                string userName = Request.Query.Tor?.ToString();    
                string password = Request.Query.Freja?.ToString();
                var result = MembershipService.ValidateUser(userName, password);
                if (result != ValidationResult.Success)
                {
                    return Response.AsJson(result, HttpStatusCode.Forbidden);
                }

                var userModel = MembershipService.GetUserModel(userName);
                var roles = RoleProvider.GetRolesForUser(userModel.Id);
                var teams = TeamRepository.GetTeams(userModel.Id)?.Select(x=>new Team
                {
                    Id = x.Id,
                    Name = x.Name,
                }).ToArray();
                var token = Tokenizer.Encode();
                var vm = new LoginResultModel
                {
                    Result = result,
                    UserModel = userModel,
                    Roles = roles,
                    Teams = teams,
                    Token = token
                };
                return Response.AsJson(vm);
            }
            catch (Exception exception)
            {
                return Response.AsJson(new LoginResultModel{Exception = exception.Message}, HttpStatusCode.Forbidden);
            }
        }
    }
}