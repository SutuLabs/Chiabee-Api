namespace UnitTests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using FluentAssertions;
    using Newtonsoft.Json;
    using Xunit;
    using static WebApi.Services.RsyncScheduleService;

    public class ExecutionPlanTest
    {
        [Fact]
        public void BasicTest()
        {
            var planParam = JsonConvert.DeserializeObject<ExecutionPlanParameter>(File.ReadAllText(@"Plans/plan1.json"));
            var plans = GetExecutionPlan(planParam).ToArray();
            plans.Should().BeEquivalentTo(new ExecutionRsyncPlan[] { });

            var p = planParam.plotters[0];
            planParam.plotters[0] = p with { MadmaxJob = p.MadmaxJob with { Job = p.MadmaxJob.Job with { CopyingFile = null, CopyingPercent = null, CopyingSpeed = null, CopyingTarget = null } } };

            plans = GetExecutionPlan(planParam).ToArray();
            var result = JsonConvert.SerializeObject(plans);
            //var cplan = JsonConvert.DeserializeObject<ExecutionRsyncPlan[]>(@"[{""FromHost"":""r720p01-65"",""PlotFilePath"":""/data/final/plot-k32-2021-06-19-18-34-10907003c9a5d2ab29cdbb3417e23958edb3a0f1e783ae04ae8a51220da7e609.plot"",""ToHost"":""10.179.0.228"",""DiskName"":""A136""}]");
            var cplan = new ExecutionRsyncPlan("r720p01-65", "/data/final/plot-k32-2021-06-19-18-34-10907003c9a5d2ab29cdbb3417e23958edb3a0f1e783ae04ae8a51220da7e609.plot", "10.179.0.228", "A136");
            //plans.Should().BeEquivalentTo(cplan)
            plans.Should().HaveCount(1);
            var pp = plans.First();
            pp.FromHost.Should().Be(cplan.FromHost);
            pp.ToHost.Should().Be(cplan.ToHost);
            pp.PlotFilePath.Should().Be(cplan.PlotFilePath);
            // disk is random
        }

        [Fact]
        public void BasicTest2()
        {
            //var planParam = JsonConvert.DeserializeObject<ExecutionPlanParameter>(File.ReadAllText(@"Plans/plan2.json"));
            //var plans = GetExecutionPlan(planParam).ToArray();

            //var cplan = new ExecutionRsyncPlan("r720p01-65", "/data/final/plot-k32-2021-06-19-18-34-10907003c9a5d2ab29cdbb3417e23958edb3a0f1e783ae04ae8a51220da7e609.plot", "10.179.0.228", "A136");
            //plans.Should().HaveCount(1);
            //var pp = plans.First();
            //pp.FromHost.Should().Be(cplan.FromHost);
            //pp.ToHost.Should().Be(cplan.ToHost);
            //pp.PlotFilePath.Should().Be(cplan.PlotFilePath);
            // disk is random
        }
    }
}
