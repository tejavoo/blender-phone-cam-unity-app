using System.Runtime.CompilerServices;

// Lets the EditMode test assembly exercise the pipeline's pure static math
// helpers (axis conversion, Euler decomposition, unwrapping, etc.) directly,
// not just the end-to-end Process() behaviour.
[assembly: InternalsVisibleTo("CamLinkPro.Tests")]
