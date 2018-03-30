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
using Vem.Common.Utilities.Interfaces.Wrapper;
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
        private readonly IDnsWrapper DnsWrapper;
        private readonly string _privateKey;

        public SecurityModule(IMembershipService membershipService, IRoleProvider roleProvider, ITeamRepository teamRepository, ITokenizer tokenizer, IDnsWrapper dnsWrapper)
        {
            MembershipService = membershipService;
            RoleProvider = roleProvider;
            TeamRepository = teamRepository;
            Tokenizer = tokenizer;
            DnsWrapper = dnsWrapper;

            Post["Security/login"] = _ => Login();
            Post["Security/IsAuthorized"] = _ => IsAuthorized();
            Post["Security/IsAdministrator"] = _ => IsAdministrator();
            Post["Security/IsAgentAuthorized"] = _ => IsAgentAuthorized();
            _privateKey = ConfigurationManager.AppSettings["TokenRawData"];
        }

        private Response IsAuthorized()
        {
            try
            {
                var actual = GetAuthorizationToken();
                return actual == null ? HttpStatusCode.Forbidden : HttpStatusCode.OK;

            }
            catch (Exception exception)
            {
                return Response.AsJson(new LoginResultModel { Exception = exception.Message }, HttpStatusCode.Forbidden);
            }
        }

        private Response IsAdministrator()
        {
            try
            {
                var actual = GetAuthorizationToken();
                if (actual == null)
                {
                    return HttpStatusCode.Forbidden;
                }
                var response = Tokenizer.Encode(new IsResponse {Is = actual.IsAdmin }, UserHostAddress());
                return Response.AsJson(response);
            }
            catch (Exception exception)
            {
                return Response.AsJson(new LoginResultModel { Exception = exception.Message }, HttpStatusCode.Forbidden);
            }
        }

        private Response IsAgentAuthorized()
        {
            var actual = GetAuthorizationToken();
            if (actual == null)
            {
                return HttpStatusCode.Forbidden;
            }
            var response = Tokenizer.Encode(new IsResponse { Is = actual.IsAgentAuthorized }, UserHostAddress());
            return Response.AsJson(response);
        }

        private  Response Login()
        {
            try
            {
                //e.g. http://localhost:8080/Bonobo.Git.Server/api/security/login?Tor=admin&Freja=admin
                string token = Request.Query.Token?.ToString();
                var requestModel = Tokenizer.Decode<VemLoginRequest>(token, UserHostAddress());
                
                var userName = requestModel.Value.Tor;
                var password = requestModel.Value.Freja;
                Log.Debug($"Login received for {userName}");
                var result = MembershipService.ValidateUser(userName, password);
                Log.Debug($"Login result for user: {userName} = {result}");
                if (result != ValidationResult.Success)
                {
                    return HttpStatusCode.Forbidden;
                }
                var userModel = MembershipService.GetUserModel(userName);
                
                var roles = RoleProvider.GetRolesForUser(userModel.Id).Select(x=>new Role
                {
                    Name = x
                }).ToArray();
                var teams = TeamRepository.GetTeams(userModel.Id)?.Select(x => new Team
                {
                    Guid = x.Id,
                    Name = x.Name,
                }).ToArray();
                var appUser = new AppUser
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
                    IsAgentAuthorized = teams?.Any(x=>x.Name.Equals("VEM-Agents", StringComparison.InvariantCultureIgnoreCase)) ?? false,
                };
                appUser.Token = Tokenizer.Encode(appUser, _privateKey);
                Log.Debug($"Returning model for user: {userName} = " + "{vm}", appUser);
                var response = Tokenizer.Encode(appUser, UserHostAddress());
                return Response.AsJson(response);
            }
            catch (Exception exception)
            {
                return Response.AsJson(new LoginResultModel{Exception = exception.Message}, HttpStatusCode.Forbidden);
            }
        }

        private string PrivateKeyUserHostAddress()
        {
            return $"{_privateKey}-{UserHostAddress()}";
        }

        private string UserHostAddress()
        {
            if (Request.UserHostAddress.StartsWith("1270.0.") || Request.UserHostAddress.Equals("::1"))
            {
                return DnsWrapper.HostAddressesName.First();
            }

            return Request.UserHostAddress;
        }

        private AuthorizationToken GetAuthorizationToken()
        {
            string token = Request.Query.Token?.ToString();
            var requestModel = Tokenizer.Decode<string>(token, UserHostAddress());
            var x = requestModel.Value;
            var actual = Tokenizer.Decode<AuthorizationToken>(x, _privateKey)?.Value;
            return actual;
        }
    }
}