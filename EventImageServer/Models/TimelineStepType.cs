namespace EventImageServer.Models
{
    // Ordered lifecycle milestones tracked independently of the overall VendorStatus.
    public enum TimelineStepType
    {
        Searched,
        Talked,
        Met,
        PriceReceived,
        Negotiating,
        ContractSigned,
        DepositPaid,
        Closed
    }
}
