namespace HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

public static class MqttTopicMatcher
{
    public static bool IsMatch(string filter, string topic)
    {
        if (string.IsNullOrEmpty(filter) || string.IsNullOrEmpty(topic))
            return false;

        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');

        for (var i = 0; i < filterLevels.Length; i++)
        {
            var filterLevel = filterLevels[i];
            if (filterLevel == "#")
                return i == filterLevels.Length - 1;

            if (i >= topicLevels.Length)
                return false;

            if (filterLevel != "+" && !string.Equals(filterLevel, topicLevels[i], StringComparison.Ordinal))
                return false;
        }

        return filterLevels.Length == topicLevels.Length;
    }
}
