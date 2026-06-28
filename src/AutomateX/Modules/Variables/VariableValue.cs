namespace AutomateX.Modules.Variables;

// One variable's value for one environment. Stored plain for a non-secret variable; for a secret
// variable, Value is the workspace-DEK ciphertext (sealed by TenantCipher), decrypted only in memory
// during resolution and never returned by the read API.
public sealed class VariableValue
{
    private VariableValue()
    {
    }

    public Guid Id { get; private set; }

    public Guid VariableId { get; private set; }

    public Guid EnvironmentId { get; private set; }

    public string Value { get; private set; } = null!;

    public static VariableValue Create(Guid variableId, Guid environmentId, string value) => new()
    {
        Id = Guid.CreateVersion7(),
        VariableId = variableId,
        EnvironmentId = environmentId,
        Value = value,
    };

    public void SetValue(string value) => Value = value;
}
