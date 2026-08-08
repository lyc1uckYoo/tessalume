using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal interface ICreatorWorkflowEvaluator
{
    CreatorWorkflowSnapshot Evaluate(ThemeProjectSnapshot project);
}
