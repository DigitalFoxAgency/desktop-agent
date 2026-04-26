namespace AgentDesktop.Domain.Scenarios;

/// <summary>
/// Discriminated union describing where a scenario step's input value
/// comes from: a literal, a scenario-level input, or an output of a
/// prior step.
/// </summary>
public abstract record ScenarioBinding
{
    private ScenarioBinding()
    {
    }

    /// <summary>Literal value embedded in the scenario YAML/JSON.</summary>
    public sealed record Literal(object? Value) : ScenarioBinding;

    /// <summary>Reference to a top-level scenario input by name (<c>$input.foo</c>).</summary>
    public sealed record ScenarioInput : ScenarioBinding
    {
        public ScenarioInput(string inputName)
        {
            ArgumentNullException.ThrowIfNull(inputName);
            if (string.IsNullOrWhiteSpace(inputName))
            {
                throw new ArgumentException("Input name cannot be empty.", nameof(inputName));
            }

            InputName = inputName;
        }

        public string InputName { get; }
    }

    /// <summary>Reference to a prior step's output by name (<c>$step[N].outputs.foo</c>).</summary>
    public sealed record PriorStepOutput : ScenarioBinding
    {
        public PriorStepOutput(int stepIndex, string outputName)
        {
            if (stepIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepIndex), "Step index must be ≥ 0.");
            }

            ArgumentNullException.ThrowIfNull(outputName);
            if (string.IsNullOrWhiteSpace(outputName))
            {
                throw new ArgumentException("Output name cannot be empty.", nameof(outputName));
            }

            StepIndex = stepIndex;
            OutputName = outputName;
        }

        public int StepIndex { get; }
        public string OutputName { get; }
    }
}
