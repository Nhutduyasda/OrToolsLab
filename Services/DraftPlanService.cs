using Microsoft.Extensions.Caching.Memory;
using OrToolsLab.Models;

namespace OrToolsLab.Services;

public class DraftPlanService
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(1);

    public DraftPlanService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string SaveDraftPlan(PlanResult plan)
    {
        // For Draft plans, ensure they have an ID.
        if (string.IsNullOrEmpty(plan.Id))
        {
            plan.Id = Guid.NewGuid().ToString();
        }
        
        _cache.Set(plan.Id, plan, _cacheDuration);
        return plan.Id;
    }

    public PlanResult? GetDraftPlan(string planId)
    {
        _cache.TryGetValue(planId, out PlanResult? plan);
        return plan;
    }

    public void RemoveDraftPlan(string planId)
    {
        _cache.Remove(planId);
    }
}
