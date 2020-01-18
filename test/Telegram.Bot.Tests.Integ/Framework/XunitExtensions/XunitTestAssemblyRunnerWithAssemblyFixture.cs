﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Telegram.Bot.Tests.Integ.Framework.XunitExtensions
{
    public class XunitTestAssemblyRunnerWithAssemblyFixture : XunitTestAssemblyRunner
    {
        readonly Dictionary<Type, object> _assemblyFixtureMappings = new Dictionary<Type, object>();

        public XunitTestAssemblyRunnerWithAssemblyFixture(
            ITestAssembly testAssembly,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions)
            : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
        { }

        protected override async Task AfterTestAssemblyStartingAsync()
        {
            // Let everything initialize
            await base.AfterTestAssemblyStartingAsync();

            // Go find all the AssemblyFixtureAttributes adorned on the test assembly
            Aggregator.Run(() =>
            {
                var fixturesAttrs = ((IReflectionAssemblyInfo)TestAssembly.Assembly)
                    .Assembly
                    .GetCustomAttributes(typeof(AssemblyFixtureAttribute))
                    .Cast<AssemblyFixtureAttribute>()
                    .ToList();

                // Instantiate all the fixtures
                foreach (var fixtureAttr in fixturesAttrs)
                {
                    var fixtureInstance = Activator.CreateInstance(fixtureAttr.FixtureType);
                    _assemblyFixtureMappings[fixtureAttr.FixtureType] = fixtureInstance;
                }
            });
        }

        protected override Task BeforeTestAssemblyFinishedAsync()
        {
            // Make sure we clean up everybody who is disposable, and use Aggregator.Run to isolate Dispose failures
            foreach (var disposable in _assemblyFixtureMappings.Values.OfType<IDisposable>())
            {
                Aggregator.Run(disposable.Dispose);
            }

            return base.BeforeTestAssemblyFinishedAsync();
        }

        protected override async Task<RunSummary> RunTestCollectionAsync(
            IMessageBus messageBus,
            ITestCollection testCollection,
            IEnumerable<IXunitTestCase> testCases,
            CancellationTokenSource cancellationTokenSource)
        {
            var exceptionAggregator = new ExceptionAggregator(Aggregator);
            var runner =  new XunitTestCollectionRunnerWithAssemblyFixture(
                _assemblyFixtureMappings,
                testCollection,
                testCases,
                DiagnosticMessageSink,
                messageBus,
                TestCaseOrderer,
                exceptionAggregator,
                cancellationTokenSource
            );

            var runSummary = await runner.RunAsync();

            var testsFixture = (TestsFixture) _assemblyFixtureMappings.Single().Value;
            testsFixture.RunSummary.Aggregate(runSummary);

            return runSummary;
        }
    }
}
