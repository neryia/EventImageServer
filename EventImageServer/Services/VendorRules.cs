using EventImageServer.Models;

namespace EventImageServer.Services
{
    // Server-side rules that keep a vendor's derived fields consistent:
    // the timeline is a contiguous prefix, Status follows the timeline
    // (unless Cancelled), and PaidAmount / NextPaymentDate follow the
    // payment schedule.
    public static class VendorRules
    {
        // Mirrors the client's former TIMELINE_STEP_TO_STATUS table.
        public static VendorStatus DeriveStatus(IEnumerable<VendorTimelineStep>? timeline)
        {
            var highest = (timeline ?? Enumerable.Empty<VendorTimelineStep>())
                .Where(s => s.IsDone)
                .Select(s => (TimelineStepType?)s.Step)
                .OrderByDescending(s => s)
                .FirstOrDefault();

            return highest switch
            {
                null => VendorStatus.NotStarted,
                TimelineStepType.Searched => VendorStatus.Searching,
                TimelineStepType.Talked => VendorStatus.Contacted,
                TimelineStepType.Met => VendorStatus.MeetingScheduled,
                TimelineStepType.PriceReceived => VendorStatus.PriceReceived,
                TimelineStepType.Negotiating => VendorStatus.Negotiating,
                TimelineStepType.ContractSigned => VendorStatus.ContractSigned,
                // Deposit Paid / Closed steps are no longer used; legacy ones count as signed.
                TimelineStepType.DepositPaid => VendorStatus.ContractSigned,
                TimelineStepType.Closed => VendorStatus.ContractSigned,
                _ => VendorStatus.NotStarted
            };
        }

        // Checking a step checks every earlier step; unchecking a step
        // unchecks every later one. Returns true when anything changed.
        public static bool ApplyTimelineChange(Vendor vendor, TimelineStepType target, bool isDone)
        {
            var changed = false;
            foreach (var step in vendor.Timeline ?? Enumerable.Empty<VendorTimelineStep>())
            {
                var shouldBeDone = isDone ? step.Step <= target : step.Step < target;
                if (step.IsDone == shouldBeDone)
                {
                    continue;
                }

                step.IsDone = shouldBeDone;
                step.CompletedAt = shouldBeDone ? DateTime.UtcNow : null;
                changed = true;
            }

            if (vendor.Status != VendorStatus.Cancelled)
            {
                vendor.Status = DeriveStatus(vendor.Timeline);
            }

            return changed;
        }

        public static void RecalculatePayments(Vendor vendor)
        {
            var payments = vendor.Payments ?? new List<VendorPayment>();
            vendor.PaidAmount = payments.Where(p => p.IsPaid).Sum(p => p.Amount);
            vendor.NextPaymentDate = payments
                .Where(p => !p.IsPaid)
                .Select(p => (DateTime?)p.DueDate)
                .OrderBy(d => d)
                .FirstOrDefault();
        }
    }
}
