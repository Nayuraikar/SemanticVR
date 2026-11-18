namespace SemanticVR.Environment
{
    /// <summary>
    /// Coarse semantic classes of the traffic scene. Used in later phases as
    /// ground-truth labels and as keys for semantic-importance weights.
    /// </summary>
    public enum SemanticCategory
    {
        Ground = 0,
        Road = 1,
        RoadMarking = 2,
        Sidewalk = 3,
        TrafficLight = 4,
        TrafficSign = 5,
        Vehicle = 6,
        Pedestrian = 7,
        Building = 8,
        Vegetation = 9
    }
}
