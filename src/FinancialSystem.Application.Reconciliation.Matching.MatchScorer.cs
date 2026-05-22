// ...existing code...
    public MatchConfidence DetermineConfidence(double totalScore)
    {
        if (double.IsNaN(totalScore))
            return MatchConfidence.None;

        if (totalScore >= _opts.HighConfidenceThreshold)
            return MatchConfidence.High;

        if (totalScore >= _opts.MediumConfidenceThreshold)
            return MatchConfidence.Medium;

        if (totalScore >= _opts.NearMissThreshold)
            return MatchConfidence.Low;

        return MatchConfidence.None;
    }
// ...existing code...
