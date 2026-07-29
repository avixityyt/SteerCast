using SteerCast.Core.Models;

namespace SteerCast.App.Services;

public interface IForceFeedbackStatusSource
{
    ForceFeedbackReading ForceFeedbackStatus { get; }
}
