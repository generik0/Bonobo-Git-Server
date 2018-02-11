using System;
using System.Configuration;
using System.Text;
using Bonobo.Git.Server.Models;
using Jose;

namespace Bonobo.Git.Server.Security
{
    public class Tokenizer : ITokenizer
    {
        private readonly byte[] _sigyn;

        public Tokenizer()
        {
            var appSetting = ConfigurationManager.AppSettings["TokenRawData"];
            _sigyn = Encoding.Unicode.GetBytes(appSetting) ;
            
        }

        public VemToken Encode()
        {
            var creation = DateTime.UtcNow;
            

            var actual = JWT.Encode(creation.ToString("o"), _sigyn, JwsAlgorithm.HS256);
            return new VemToken {Token = actual};
        }

        public bool Decode(VemToken token)
        {
            var payload = JWT.Decode(token.Token, _sigyn);
            if (!DateTime.TryParse(payload, out var actual))
            {
                return false;
            }
            return actual < DateTime.UtcNow;
        }
    }
}