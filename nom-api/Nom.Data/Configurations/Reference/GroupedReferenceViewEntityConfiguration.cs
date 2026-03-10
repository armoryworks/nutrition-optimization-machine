using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Reference;

namespace Nom.Data.Configurations.Reference;

public class GroupedReferenceViewEntityConfiguration : IEntityTypeConfiguration<GroupedReferenceViewEntity>
{
    public void Configure(EntityTypeBuilder<GroupedReferenceViewEntity> builder)
    {
        builder.ToView("ReferenceGroupView", "reference");
        builder.HasNoKey();

        // The Id property is not mapped to the database view
        builder.Ignore(e => e.Id);

        // TPH discriminator based on GroupId
        builder.HasDiscriminator<long>(g => g.GroupId)
            .HasValue<MealTypeViewEntity>((long)ReferenceDiscriminatorEnum.MealType)
            .HasValue<RecipeTypeViewEntity>((long)ReferenceDiscriminatorEnum.RecipeType)
            .HasValue<ShoppingStatusTypeViewEntity>((long)ReferenceDiscriminatorEnum.ShoppingStatusType)
            .HasValue<ItemStatusTypeViewEntity>((long)ReferenceDiscriminatorEnum.ItemStatusType)
            .HasValue<RestrictionTypeViewEntity>((long)ReferenceDiscriminatorEnum.RestrictionType)
            .HasValue<GoalTypeViewEntity>((long)ReferenceDiscriminatorEnum.GoalType)
            .HasValue<NutrientTypeViewEntity>((long)ReferenceDiscriminatorEnum.NutrientType)
            .HasValue<CuisineTypeViewEntity>((long)ReferenceDiscriminatorEnum.CuisineType)
            .HasValue<PlanInvitationRoleViewEntity>((long)ReferenceDiscriminatorEnum.PlanInvitationRole)
            .HasValue<PrivacyConsentTypeViewEntity>((long)ReferenceDiscriminatorEnum.PrivacyConsentType)
            .HasValue<CurationStatusTypeViewEntity>((long)ReferenceDiscriminatorEnum.CurationStatusType)
            .HasValue<FeedbackEntityTypeViewEntity>((long)ReferenceDiscriminatorEnum.FeedbackEntityType)
            .HasValue<FeedbackTypeViewEntity>((long)ReferenceDiscriminatorEnum.FeedbackType)
            .HasValue<ShoppingPriorityTypeViewEntity>((long)ReferenceDiscriminatorEnum.ShoppingPriorityType)
            .HasValue<ShoppingCategoryTypeViewEntity>((long)ReferenceDiscriminatorEnum.ShoppingCategoryType)
            .HasValue<RecipeDifficultyTypeViewEntity>((long)ReferenceDiscriminatorEnum.RecipeDifficultyType)
            .HasValue<PersonActivityLevelTypeViewEntity>((long)ReferenceDiscriminatorEnum.PersonActivityLevelType)
            .HasValue<PersonHealthGoalTypeViewEntity>((long)ReferenceDiscriminatorEnum.PersonHealthGoalType)
            .HasValue<PersonAttributeTypeViewEntity>((long)ReferenceDiscriminatorEnum.PersonAttributeType)
            .HasValue<DayOfWeekTypeViewEntity>((long)ReferenceDiscriminatorEnum.DayOfWeekType)
            .HasValue<PersonDietaryRestrictionTypeViewEntity>((long)ReferenceDiscriminatorEnum.PersonDietaryRestrictionType)
            .HasValue<AllergyTypeViewEntity>((long)ReferenceDiscriminatorEnum.AllergyType)
            .HasValue<MedicalConditionTypeViewEntity>((long)ReferenceDiscriminatorEnum.MedicalConditionType)
            .HasValue<SocietalRestrictionTypeViewEntity>((long)ReferenceDiscriminatorEnum.SocietalRestrictionType)
            .HasValue<PersonalPreferenceTypeViewEntity>((long)ReferenceDiscriminatorEnum.PersonalPreferenceType);
    }
}
