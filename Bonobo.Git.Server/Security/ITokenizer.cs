using Bonobo.Git.Server.Models;

namespace Bonobo.Git.Server.Security
{
    public interface ITokenizer
    {
        bool Decode(string token);
        VemToken Encode();
    }
}