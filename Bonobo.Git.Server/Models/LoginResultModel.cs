using System.Collections.Generic;
using Bonobo.Git.Server.Data;
using Bonobo.Git.Server.Security;

namespace Bonobo.Git.Server.Models
{
    public class LoginResultModel
    {
        public UserModel UserModel { get; set; }
        public ValidationResult Result { get; set; }
        public ICollection<string> Roles { get; set; }
        public ICollection<Team> Teams { get; set; }
        public string Exception { get; set; }
    }
}