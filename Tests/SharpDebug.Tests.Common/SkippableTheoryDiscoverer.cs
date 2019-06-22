﻿using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SharpDebug.Tests
{
    public class SkippableTheoryDiscoverer : IXunitTestCaseDiscoverer
    {
        readonly IMessageSink diagnosticMessageSink;
        readonly TheoryDiscoverer theoryDiscoverer;

        public SkippableTheoryDiscoverer(IMessageSink diagnosticMessageSink)
        {
            this.diagnosticMessageSink = diagnosticMessageSink;

            theoryDiscoverer = new TheoryDiscoverer(diagnosticMessageSink);
        }

        public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
        {
            var defaultMethodDisplay = discoveryOptions.MethodDisplayOrDefault();

            // Unlike fact discovery, the underlying algorithm for theories is complex, so we let the theory discoverer
            // do its work, and do a little on-the-fly conversion into our own test cases.
            return theoryDiscoverer.Discover(discoveryOptions, testMethod, factAttribute)
                                   .Select(testCase => testCase is XunitTheoryTestCase
                                                           ? (IXunitTestCase)new SkippableTheoryTestCase(diagnosticMessageSink, defaultMethodDisplay, TestMethodDisplayOptions.All, testCase.TestMethod)
                                                           : new SkippableFactTestCase(diagnosticMessageSink, defaultMethodDisplay, TestMethodDisplayOptions.All, testCase.TestMethod, testCase.TestMethodArguments));
        }
    }
}
