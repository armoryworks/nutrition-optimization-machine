// Nom.Orch/Enums/AnswerTypeEnum.cs
namespace Nom.Orch.Enums.Question
{
    /// <summary>
    /// Represents the distinct types of answers recognized by the application logic.
    /// This is separate from ReferenceDiscriminatorEnum, which categorizes reference groups.
    /// </summary>
    public enum AnswerTypeEnum
    {
        Unknown = 0, // Default or unmapped type
        YesNo = 1,
        TextInput = 2,
        MultiSelect = 3,
        SingleSelect = 4
    }
}