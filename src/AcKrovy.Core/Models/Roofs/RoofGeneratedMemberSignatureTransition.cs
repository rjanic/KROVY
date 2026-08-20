using AcKrovy.Core.Models;

namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofGeneratedMemberSignatureTransition(
    TimberElementSignature OldSignature,
    TimberElementSignature NewSignature);
