namespace VoiceAgent.Host.Api.Campaign;

public static class CampaignStages
{
    public const string FinalConfirm = "FinalConfirm";

    public static class FE
    {
        public const string Greeting = "Greeting";
        public const string Consent = "Consent";
        public const string QualifyAge = "QualifyAge";
        public const string QualifyState = "QualifyState";
        public const string QualifyCoverage = "QualifyCoverage";
        public const string SetCallback = "SetCallback";
        public const string End = "End";

        public static readonly string[] Order = { Greeting, Consent, QualifyAge, QualifyState, QualifyCoverage, SetCallback, FinalConfirm, End };
    }

    public static class ACA
    {
        public const string Greeting = "Greeting";
        public const string Consent = "Consent";
        public const string QualifyState = "QualifyState";
        public const string QualifyHousehold = "QualifyHousehold";
        public const string QualifyIncomeRange = "QualifyIncomeRange";
        public const string QualifyCoverage = "QualifyCoverage";
        public const string SetCallback = "SetCallback";
        public const string End = "End";

        public static readonly string[] Order = { Greeting, Consent, QualifyState, QualifyHousehold, QualifyIncomeRange, QualifyCoverage, SetCallback, FinalConfirm, End };
    }

    public static class Medicare
    {
        public const string Greeting = "Greeting";
        public const string Consent = "Consent";
        public const string QualifyState = "QualifyState";
        public const string ConfirmMedicare = "ConfirmMedicare";
        public const string PartsABCheck = "PartsABCheck";
        public const string SetCallback = "SetCallback";
        public const string End = "End";

        public static readonly string[] Order = { Greeting, Consent, QualifyState, ConfirmMedicare, PartsABCheck, SetCallback, FinalConfirm, End };
    }

    public static class Solar
    {
        public const string Greeting = "Greeting";
        public const string Consent = "Consent";
        public const string QualifyState = "QualifyState";
        public const string HomeOwnerCheck = "HomeOwnerCheck";
        public const string RoofTypeOrUtilityBill = "RoofTypeOrUtilityBill";
        public const string SetCallback = "SetCallback";
        public const string End = "End";

        public static readonly string[] Order = { Greeting, Consent, QualifyState, HomeOwnerCheck, RoofTypeOrUtilityBill, SetCallback, FinalConfirm, End };
    }

    public static class AutoCare
    {
        public const string Greeting = "Greeting";
        public const string Consent = "Consent";
        public const string QualifyState = "QualifyState";
        public const string VehicleInfo = "VehicleInfo";
        public const string CurrentCoverage = "CurrentCoverage";
        public const string SetCallback = "SetCallback";
        public const string End = "End";

        public static readonly string[] Order = { Greeting, Consent, QualifyState, VehicleInfo, CurrentCoverage, SetCallback, FinalConfirm, End };
    }

    public static class DoctorAppt
    {
        public const string Greeting = "Greeting";
        public const string IdentifyNeed = "IdentifyNeed";
        public const string CollectPatientName = "CollectPatientName";
        public const string CollectPreferredDate = "CollectPreferredDate";
        public const string CollectPreferredTime = "CollectPreferredTime";
        public const string ConfirmCallbackNumber = "ConfirmCallbackNumber";
        public const string End = "End";

        public static readonly string[] Order = { Greeting, IdentifyNeed, CollectPatientName, CollectPreferredDate, CollectPreferredTime, ConfirmCallbackNumber, FinalConfirm, End };
    }

    public static class CabBooking
    {
        public const string Greeting = "Greeting";
        public const string IdentifyNeed = "IdentifyNeed";
        public const string CollectPickupLocation = "CollectPickupLocation";
        public const string CollectDropoffLocation = "CollectDropoffLocation";
        public const string CollectPickupTime = "CollectPickupTime";
        public const string OptionalPassengers = "OptionalPassengers";
        public const string ConfirmCallbackNumber = "ConfirmCallbackNumber";
        public const string End = "End";

        public static readonly string[] Order = { Greeting, IdentifyNeed, CollectPickupLocation, CollectDropoffLocation, CollectPickupTime, OptionalPassengers, ConfirmCallbackNumber, FinalConfirm, End };
    }

    public static string[] GetOrderForCampaign(string campaignCode)
    {
        return campaignCode.ToUpperInvariant() switch
        {
            "FE" => FE.Order,
            "ACA" => ACA.Order,
            "MEDICARE" => Medicare.Order,
            "SOLAR" => Solar.Order,
            "AUTOCARE" => AutoCare.Order,
            "DOCTOR_APPT" => DoctorAppt.Order,
            "CAB_BOOKING" => CabBooking.Order,
            _ => FE.Order
        };
    }
}
