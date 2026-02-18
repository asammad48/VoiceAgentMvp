namespace VoiceAgent.Host.Api.Campaign;

public sealed record NextStep(string NextStage, string? NextQuestionKey = null, string? RequiredFieldKey = null);

public interface INextStepPlanner
{
    NextStep PlanNext(string? campaignCode, string currentStage, IReadOnlyDictionary<string, string> fields);
}

public sealed class NextStepPlanner : INextStepPlanner
{
    public NextStep PlanNext(string? campaignCode, string currentStage, IReadOnlyDictionary<string, string> fields)
    {
        var code = campaignCode?.ToUpperInvariant() ?? "FE";
        var order = CampaignStages.GetOrderForCampaign(code);
        var currentIndex = Array.IndexOf(order, currentStage);

        if (currentIndex == -1) currentIndex = 0;

        // If the current stage has a required field and it's still missing, stay here.
        var requiredField = GetFieldForStage(code, currentStage);
        if (requiredField != null && !fields.ContainsKey(requiredField))
        {
            return new NextStep(currentStage, currentStage, requiredField);
        }

        // If the current stage is completed (either field is present or it has no field),
        // move to the next stage that is not yet completed.
        for (int i = currentIndex + 1; i < order.Length; i++)
        {
            var stage = order[i];
            var field = GetFieldForStage(code, stage);

            if (field == null || !fields.ContainsKey(field))
            {
                return new NextStep(stage, stage, field);
            }
        }

        return new NextStep(order[^1], order[^1], null);
    }

    private string? GetFieldForStage(string campaignCode, string stage)
    {
        return (campaignCode, stage) switch
        {
            ("FE", CampaignStages.FE.Consent) => "consent",
            ("FE", CampaignStages.FE.QualifyAge) => "age_range",
            ("FE", CampaignStages.FE.QualifyState) => "state",
            ("FE", CampaignStages.FE.QualifyCoverage) => "has_coverage",
            ("FE", CampaignStages.FE.SetCallback) => "callback_time",

            ("ACA", CampaignStages.ACA.Consent) => "consent",
            ("ACA", CampaignStages.ACA.QualifyState) => "state",
            ("ACA", CampaignStages.ACA.QualifyHousehold) => "household_size",
            ("ACA", CampaignStages.ACA.QualifyIncomeRange) => "income_range",
            ("ACA", CampaignStages.ACA.QualifyCoverage) => "has_coverage",
            ("ACA", CampaignStages.ACA.SetCallback) => "callback_time",

            ("MEDICARE", CampaignStages.Medicare.Consent) => "consent",
            ("MEDICARE", CampaignStages.Medicare.QualifyState) => "state",
            ("MEDICARE", CampaignStages.Medicare.ConfirmMedicare) => "has_medicare",
            ("MEDICARE", CampaignStages.Medicare.PartsABCheck) => "parts_ab",
            ("MEDICARE", CampaignStages.Medicare.SetCallback) => "callback_time",

            ("SOLAR", CampaignStages.Solar.Consent) => "consent",
            ("SOLAR", CampaignStages.Solar.QualifyState) => "state",
            ("SOLAR", CampaignStages.Solar.HomeOwnerCheck) => "home_owner",
            ("SOLAR", CampaignStages.Solar.RoofTypeOrUtilityBill) => "monthly_bill",
            ("SOLAR", CampaignStages.Solar.SetCallback) => "callback_time",

            ("AUTOCARE", CampaignStages.AutoCare.Consent) => "consent",
            ("AUTOCARE", CampaignStages.AutoCare.QualifyState) => "state",
            ("AUTOCARE", CampaignStages.AutoCare.VehicleInfo) => "car_year_make",
            ("AUTOCARE", CampaignStages.AutoCare.CurrentCoverage) => "has_insurance",
            ("AUTOCARE", CampaignStages.AutoCare.SetCallback) => "callback_time",

            ("DOCTOR_APPT", CampaignStages.DoctorAppt.IdentifyNeed) => "needs_appointment",
            ("DOCTOR_APPT", CampaignStages.DoctorAppt.CollectPatientName) => "patient_name",
            ("DOCTOR_APPT", CampaignStages.DoctorAppt.CollectPreferredDate) => "preferred_date",
            ("DOCTOR_APPT", CampaignStages.DoctorAppt.CollectPreferredTime) => "preferred_time",
            ("DOCTOR_APPT", CampaignStages.DoctorAppt.ConfirmCallbackNumber) => "callback_number",

            ("CAB_BOOKING", CampaignStages.CabBooking.IdentifyNeed) => "needs_cab",
            ("CAB_BOOKING", CampaignStages.CabBooking.CollectPickupLocation) => "pickup_location",
            ("CAB_BOOKING", CampaignStages.CabBooking.CollectDropoffLocation) => "dropoff_location",
            ("CAB_BOOKING", CampaignStages.CabBooking.CollectPickupTime) => "pickup_time",
            ("CAB_BOOKING", CampaignStages.CabBooking.OptionalPassengers) => "passengers",
            ("CAB_BOOKING", CampaignStages.CabBooking.ConfirmCallbackNumber) => "callback_number",

            _ => null
        };
    }
}
