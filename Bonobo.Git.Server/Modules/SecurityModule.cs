using System;
using System.Diagnostics.CodeAnalysis;
using Bonobo.Git.Server.Models;
using Bonobo.Git.Server.Security;
using Nancy;

namespace Bonobo.Git.Server.Modules
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class SecurityModule : NancyModule
    {
        private readonly IMembershipService MembershipService;
        public readonly IRoleProvider RoleProvider;

        public SecurityModule(IMembershipService membershipService, IRoleProvider roleProvider)
        {
            MembershipService = membershipService;
            RoleProvider = roleProvider;
            Post["Security/login"] = _ => Test();
        }

        private  Response Test()
        {
            try
            {
                //e.g.http://localhost:51233/api/security/login?Username=admin&Password=admin
                string userName = Request.Query.Username?.ToString();    
                string password = Request.Query.Password?.ToString();
                ValidationResult result = MembershipService.ValidateUser(userName, password);
                if (result != ValidationResult.Success)
                {
                    return Response.AsJson(result, HttpStatusCode.Forbidden);
                }

                UserModel userModel = MembershipService.GetUserModel(userName);
                var roles = RoleProvider.GetRolesForUser(userModel.Id);
                return Response.AsJson((result, userModel, roles));
            }
            catch (Exception exception)
            {
                return Response.AsJson(exception, HttpStatusCode.Forbidden);
            }
            
        }
    }
}