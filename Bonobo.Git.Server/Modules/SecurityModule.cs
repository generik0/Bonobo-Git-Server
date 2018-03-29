using System;
using System.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Bonobo.Git.Server.Data;
using Bonobo.Git.Server.Models;
using Bonobo.Git.Server.Security;
using Nancy;
using Serilog;
using Vem.Common.Dtos.Securities;
using Vem.Common.Utilities.Interfaces.Tokens;
using Team = Vem.Common.Dtos.Securities.Team;
using Role = Vem.Common.Dtos.Securities.Role;

namespace Bonobo.Git.Server.Modules
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class SecurityModule : NancyModule
    {
        private readonly IMembershipService MembershipService;
        private readonly IRoleProvider RoleProvider;
        private readonly ITeamRepository TeamRepository;
        private readonly ITokenizer Tokenizer;
        private readonly string _privateKey;

        public SecurityModule(IMembershipService membershipService, IRoleProvider roleProvider, ITeamRepository teamRepository, ITokenizer tokenizer)
        {
            MembershipService = membershipService;
            RoleProvider = roleProvider;
            TeamRepository = teamRepository;
            Tokenizer = tokenizer;
            Post["Security/login"] = _ => Login();
            Post["Security/Authorize"] = _ => IsAuthorized();
            _privateKey = ConfigurationManager.AppSettings["TokenRawData"];
        }

        private Response IsAuthorized()
        {
            try
            {
                var token = Request.Query?.Token?.ToString();
                var actual = Tokenizer.Decode<AuthorizationToken>(token, _privateKey);
                return actual!=null ? HttpStatusCode.Accepted : HttpStatusCode.Forbidden;

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
                Log.Debug($"Login received for {userName}");
                string password = Request.Query.Freja?.ToString();
                var result = MembershipService.ValidateUser(userName, password);
                Log.Debug($"Login result for user: {userName} = {result}");
                if (result != ValidationResult.Success)
                {
                    return Response.AsJson(result, HttpStatusCode.Forbidden);
                }
                var userModel = MembershipService.GetUserModel(userName);


                var roles = RoleProvider.GetRolesForUser(userModel.Id).Select(x=>new Role
                {
                    Name = x
                }).ToArray();
                var teams = TeamRepository.GetTeams(userModel.Id)?.Select(x => new Team
                {
                    Guid = x.Id.ToString(),
                    Name = x.Name,
                }).ToArray();
                var appuser = new AppUser
                {
                    Teams = teams,
                    Roles = roles,
                    DisplayName = userModel.DisplayName,
                    GivenName = userModel.GivenName,
                    Surname = userModel.Surname,
                    Email = userModel.Email,
                    Id = userModel.Id,
                    Username = userModel.Username,
                    SortName = userModel.Username,
                    IsAdmin = roles.Any(x=>x.Name.Equals("Administrator", StringComparison.InvariantCultureIgnoreCase)),
                    IsAgentAuthorized = teams?.Any(x=>x.Name.Equals("VEM-Agents", StringComparison.InvariantCultureIgnoreCase)) ??false,
                };
                appuser.Token = Tokenizer.Encode(appuser, _privateKey);
                Log.Debug($"Returning model for user: {userName} = " + "{vm}", appuser);
                return Response.AsJson(appuser);
            }
            catch (Exception exception)
            {
                return Response.AsJson(new LoginResultModel{Exception = exception.Message}, HttpStatusCode.Forbidden);
            }
        }
    }
}