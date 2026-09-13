using UdonSharp;

namespace PureUdonGo
{
    /// <summary>Stable marker used by the editor generator and validator.</summary>
    public sealed class GoGeneratedWorld : UdonSharpBehaviour
    {
        // Cached room references and mutually exclusive central UGUI input.
        public const int CURRENT_SCHEMA=14;
        public string generatorId="PureUdonGo.FinalProduction";
        public int schemaVersion=CURRENT_SCHEMA;
    }
}
