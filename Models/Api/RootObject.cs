using System.Collections.Generic;

namespace JacBlack.Models.Api
{
    public class RootObject
    {
        public List<Result> Results { get; set; }

        public bool jacred => true;
    }
}
