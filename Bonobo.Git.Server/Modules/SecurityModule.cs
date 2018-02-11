﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Bonobo.Git.Server.Data;
using Bonobo.Git.Server.Security;
using Nancy;

namespace Bonobo.Git.Server.Modules
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class SecurityModule : NancyModule
    {
        private readonly IMembershipService MembershipService;
        private readonly IRoleProvider RoleProvider;
        private readonly ITeamRepository TeamRepository;

        public SecurityModule(IMembershipService membershipService, IRoleProvider roleProvider, ITeamRepository teamRepository)
        {
            MembershipService = membershipService;
            RoleProvider = roleProvider;
            TeamRepository = teamRepository;
            Post["Security/login"] = _ => Login();
        }

        private  Response Login()
        {
            try
            {
                //e.g.http://localhost:51233/api/security/login?Username=admin&Password=admin
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
                });
                var valueTuple = (Result: result, User: userModel, Roles: roles, Teams: teams);
                return Response.AsJson(valueTuple);
            }
            catch (Exception exception)
            {
                return Response.AsJson(exception, HttpStatusCode.Forbidden);
            }
            
        }
    }
}