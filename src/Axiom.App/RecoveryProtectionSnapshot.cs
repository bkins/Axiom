using Axiom.Core;

namespace Axiom.App;

internal sealed record RecoveryProtectionSnapshot(DocumentTab Tab, string Text, RecoveryDraft? Draft);
