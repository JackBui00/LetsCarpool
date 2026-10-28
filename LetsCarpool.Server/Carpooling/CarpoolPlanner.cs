namespace LetsCarpool.Server.Carpooling;

public sealed class CarpoolPlanner(IDistanceProvider distanceProvider, int searchOperationLimit = 100_000)
{
    public const int MaximumParticipants = 6;
    public const int MaximumSearchOperations = 100_000;
    private const string DistanceSource = "haversine-estimate";
    private const string Optimality = "exact-within-limit";
    private static readonly string[] Warnings =
    [
        "Distances use straight-line Haversine estimates and are not road routes or navigation guidance.",
        "Duration estimates assume 40 km/h and sum travel legs; traffic is not modeled.",
        "Independent rendezvous legs have modeAssumption unspecified; their durations use the same 40 km/h estimate, and no walking, transit, or road mode is inferred.",
        "Each driver's maxDetourMinutes (or the request-level default) limits vehicle-route time beyond direct origin-to-destination time; independent rendezvous travel is excluded.",
        "Exact search exhaustively covers offered-driver subsets, participant assignments, pickup orders, and participant-origin meeting candidates for pure pickup and rendezvous alternatives; mixed per-group strategies are not included.",
        $"Search is bounded to {MaximumSearchOperations} operations; exact results are returned only when the full search completes within this limit.",
        "Arrival windows and deadlines are not enforced."
    ];

    public CarpoolPlanResponse Plan(CarpoolPlanRequest request)
    {
        if (searchOperationLimit is < 1 or > MaximumSearchOperations)
            throw new ArgumentOutOfRangeException(nameof(searchOperationLimit));
        var searchBudget = new SearchBudget(searchOperationLimit);
        var participants = request.Participants;
        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var strategy in new[] { "pickup", "rendezvous" })
            candidates[strategy] = new Candidate(strategy, participants);

        var potentialDrivers = Enumerable.Range(0, participants.Count)
            .Where(index => participants[index].CanDrive ?? (participants[index].PassengerSeats > 0)).ToArray();
        var selectedDrivers = new List<int>();

        EnumerateDriverSubsets(0);

        var alternatives = candidates.Values
            .Where(candidate => candidate.HasPlan)
            .OrderByDescending(candidate => candidate.ServedCount)
            .ThenBy(candidate => candidate.TotalDistanceMeters)
            .ThenBy(candidate => candidate.Strategy, StringComparer.Ordinal)
            .Select(candidate => candidate.ToResponse())
            .ToArray();
        return new CarpoolPlanResponse(alternatives, searchBudget.Operations, searchOperationLimit);

        void EnumerateDriverSubsets(int position)
        {
            searchBudget.Consume();
            if (position == potentialDrivers.Length)
            {
                AssignRiders(0, new Dictionary<int, List<int>>());
                return;
            }
            EnumerateDriverSubsets(position + 1);
            selectedDrivers.Add(potentialDrivers[position]);
            EnumerateDriverSubsets(position + 1);
            selectedDrivers.RemoveAt(selectedDrivers.Count - 1);
        }

        void AssignRiders(int participantIndex, Dictionary<int, List<int>> groups)
        {
            searchBudget.Consume();
            while (participantIndex < participants.Count && selectedDrivers.Contains(participantIndex))
                participantIndex++;
            if (participantIndex == participants.Count)
            {
                EvaluateAssignment(groups);
                return;
            }

            // Leaving a person unassigned is a valid candidate and can be required by capacity or route constraints.
            AssignRiders(participantIndex + 1, groups);
            foreach (var driverIndex in selectedDrivers)
            {
                var riders = groups.TryGetValue(driverIndex, out var existing) ? existing : [];
                if (riders.Count >= participants[driverIndex].PassengerSeats) continue;
                if (existing is null) groups[driverIndex] = riders;
                riders.Add(participantIndex);
                AssignRiders(participantIndex + 1, groups);
                riders.RemoveAt(riders.Count - 1);
                if (riders.Count == 0) groups.Remove(driverIndex);
            }
        }

        void EvaluateAssignment(Dictionary<int, List<int>> groups)
        {
            searchBudget.Consume();
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var driverIndex in selectedDrivers) assigned.Add(participants[driverIndex].Id);
            foreach (var riders in groups.Values)
                foreach (var index in riders) assigned.Add(participants[index].Id);
            var unassigned = participants.Where(person => !assigned.Contains(person.Id)).Select(person => new UnassignedParticipant(
                person.Id,
                potentialDrivers.Length == 0 ? "No participant offered a vehicle." : "Available seats or feasible routes were insufficient.")).ToArray();
            foreach (var candidate in candidates.Values)
            {
                var strategyGroups = new List<PlanGroup>();
                var feasible = true;
                foreach (var driverIndex in selectedDrivers.Order())
                {
                    var riders = groups.TryGetValue(driverIndex, out var riderIndexes) ? riderIndexes : [];
                    var route = FindBestGroupPlanForStrategy(candidate.Strategy, participants[driverIndex], riders.Select(index => participants[index]).ToArray(), request, searchBudget);
                    if (route is null) { feasible = false; break; }
                    strategyGroups.Add(route);
                }
                if (feasible) candidate.Consider(strategyGroups, unassigned);
            }
        }
    }

    private PlanGroup? FindBestGroupPlanForStrategy(string strategy, PlanParticipant driver, PlanParticipant[] riders, CarpoolPlanRequest request, SearchBudget searchBudget)
    {
        if (riders.Length > driver.PassengerSeats) return null;
        return strategy == "pickup" ? BestPickup(driver, riders, request, searchBudget) : BestRendezvous(driver, riders, request, searchBudget);
    }

    private PlanGroup? BestPickup(PlanParticipant driver, PlanParticipant[] riders, CarpoolPlanRequest request, SearchBudget searchBudget)
    {
        var direct = distanceProvider.Estimate(driver.Origin, request.Destination);
        var maxDetourMinutes = driver.MaxDetourMinutes ?? request.MaxDetourMinutes;
        PlanGroup? best = null;
        var bestKey = string.Empty;
        var order = new List<PlanParticipant>(riders.Length);
        var used = new bool[riders.Length];
        Search(0);
        return best;

        void Search(int depth)
        {
            searchBudget.Consume();
            if (depth == riders.Length)
            {
                var current = driver.Origin;
                var vehicleDistance = 0d;
                var duration = 0d;
                var stops = new List<PlanStop>(riders.Length);
                foreach (var rider in order)
                {
                    var leg = distanceProvider.Estimate(current, rider.Origin);
                    vehicleDistance += leg.DistanceMeters;
                    duration += leg.DurationMinutes;
                    stops.Add(new PlanStop(rider.Id, rider.Origin.Label, rider.Origin.Latitude, rider.Origin.Longitude, "pickup"));
                    current = rider.Origin;
                }
                var lastLeg = distanceProvider.Estimate(current, request.Destination);
                vehicleDistance += lastLeg.DistanceMeters;
                duration += lastLeg.DurationMinutes;
                if (duration - direct.DurationMinutes > maxDetourMinutes + 1e-8) return;
                stops.Add(new PlanStop(null, request.Destination.Label, request.Destination.Latitude, request.Destination.Longitude, "destination"));
                var group = new PlanGroup(new(driver.Id, driver.Name), riders.Select(person => new PersonSummary(person.Id, person.Name)).ToArray(), stops,
                    [], vehicleDistance, 0, vehicleDistance, duration, Math.Max(0, duration - direct.DurationMinutes));
                var key = string.Join("|", order.Select(person => person.Id));
                if (best is null || group.TotalDistanceMeters < best.TotalDistanceMeters - 1e-7 ||
                    (Math.Abs(group.TotalDistanceMeters - best.TotalDistanceMeters) <= 1e-7 && string.CompareOrdinal(key, bestKey) < 0))
                { best = group; bestKey = key; }
                return;
            }
            for (var index = 0; index < riders.Length; index++)
            {
                if (used[index]) continue;
                used[index] = true;
                order.Add(riders[index]);
                Search(depth + 1);
                order.RemoveAt(order.Count - 1);
                used[index] = false;
            }
        }
    }

    private PlanGroup? BestRendezvous(PlanParticipant driver, PlanParticipant[] riders, CarpoolPlanRequest request, SearchBudget searchBudget)
    {
        if (riders.Any(rider => !rider.CanMeet)) return null;
        var direct = distanceProvider.Estimate(driver.Origin, request.Destination);
        var maxDetourMinutes = driver.MaxDetourMinutes ?? request.MaxDetourMinutes;
        PlanGroup? best = null;
        var bestKey = string.Empty;
        var meetingOptions = new[] { driver }.Concat(riders).ToArray();
        foreach (var meetingPerson in meetingOptions.OrderBy(person => person.Id, StringComparer.Ordinal))
        {
            searchBudget.Consume();
            var toMeeting = distanceProvider.Estimate(driver.Origin, meetingPerson.Origin);
            var toDestination = distanceProvider.Estimate(meetingPerson.Origin, request.Destination);
            var vehicleDuration = toMeeting.DurationMinutes + toDestination.DurationMinutes;
            if (vehicleDuration - direct.DurationMinutes > maxDetourMinutes + 1e-8) continue;
            var independentDistance = 0d;
            var independentDuration = 0d;
            var independentLegs = new List<IndependentTravelLeg>();
            foreach (var rider in riders)
            {
                var leg = distanceProvider.Estimate(rider.Origin, meetingPerson.Origin);
                independentDistance += leg.DistanceMeters;
                independentDuration += leg.DurationMinutes;
                independentLegs.Add(new IndependentTravelLeg(rider.Id,
                    new PlanStop(rider.Id, rider.Origin.Label, rider.Origin.Latitude, rider.Origin.Longitude, "origin"),
                    new PlanStop(meetingPerson.Id, meetingPerson.Origin.Label, meetingPerson.Origin.Latitude, meetingPerson.Origin.Longitude, "rendezvous"),
                    leg.DistanceMeters, leg.DurationMinutes, "unspecified"));
            }
            var vehicleDistance = toMeeting.DistanceMeters + toDestination.DistanceMeters;
            var stops = new[]
            {
                new PlanStop(meetingPerson.Id, meetingPerson.Origin.Label, meetingPerson.Origin.Latitude, meetingPerson.Origin.Longitude, "rendezvous"),
                new PlanStop(null, request.Destination.Label, request.Destination.Latitude, request.Destination.Longitude, "destination")
            };
            var group = new PlanGroup(new(driver.Id, driver.Name), riders.Select(person => new PersonSummary(person.Id, person.Name)).ToArray(), stops,
                independentLegs, vehicleDistance, independentDistance, vehicleDistance + independentDistance, vehicleDuration + independentDuration,
                Math.Max(0, vehicleDuration - direct.DurationMinutes));
            var key = meetingPerson.Id;
            if (best is null || group.TotalDistanceMeters < best.TotalDistanceMeters - 1e-7 ||
                (Math.Abs(group.TotalDistanceMeters - best.TotalDistanceMeters) <= 1e-7 && string.CompareOrdinal(key, bestKey) < 0))
            { best = group; bestKey = key; }
        }
        return best;
    }

    private sealed class SearchBudget(int limit)
    {
        public int Operations { get; private set; }

        public void Consume()
        {
            if (++Operations > limit) throw new PlannerSearchLimitException(limit);
        }
    }

    private sealed class Candidate(string strategy, IReadOnlyList<PlanParticipant> participants)
    {
        public string Strategy { get; } = strategy;
        public bool HasPlan { get; private set; }
        public int ServedCount { get; private set; } = -1;
        public double TotalDistanceMeters { get; private set; } = double.PositiveInfinity;
        private double TotalDurationMinutes { get; set; }
        private IReadOnlyList<PlanGroup> Groups { get; set; } = [];
        private IReadOnlyList<UnassignedParticipant> Unassigned { get; set; } = [];
        private string TieBreak { get; set; } = string.Empty;

        public void Consider(IReadOnlyList<PlanGroup> groups, IReadOnlyList<UnassignedParticipant> unassigned)
        {
            var served = participants.Count - unassigned.Count;
            var distance = groups.Sum(group => group.TotalDistanceMeters);
            var tieBreak = string.Join(";", groups.Select(group => $"{group.Driver.Id}:{string.Join(",", group.Riders.Select(rider => rider.Id))}:{string.Join(",", group.Stops.Select(stop => stop.ParticipantId))}"));
            if (HasPlan && (served < ServedCount || served == ServedCount &&
                (distance > TotalDistanceMeters + 1e-7 || Math.Abs(distance - TotalDistanceMeters) <= 1e-7 && string.CompareOrdinal(tieBreak, TieBreak) >= 0))) return;
            HasPlan = true;
            ServedCount = served;
            TotalDistanceMeters = distance;
            TotalDurationMinutes = groups.Sum(group => group.DurationMinutes);
            Groups = groups;
            Unassigned = unassigned;
            TieBreak = tieBreak;
        }

        public PlanAlternative ToResponse() => new(Strategy, Groups, TotalDistanceMeters, TotalDurationMinutes, Unassigned,
            DistanceSource, Optimality, Warnings);
    }
}
