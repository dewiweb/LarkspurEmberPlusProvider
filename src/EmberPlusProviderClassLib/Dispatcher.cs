﻿#region copyright
/*
 * This code is from the Lawo/ember-plus GitHub repository and is licensed with
 *
 * Boost Software License - Version 1.0 - August 17th, 2003
 *
 * Permission is hereby granted, free of charge, to any person or organization
 * obtaining a copy of the software and accompanying documentation covered by
 * this license (the "Software") to use, reproduce, display, distribute,
 * execute, and transmit the Software, and to prepare derivative works of the
 * Software, and to permit third-parties to whom the Software is furnished to
 * do so, all subject to the following:
 *
 * The copyright notices in the Software and this entire statement, including
 * the above license grant, this restriction and the following disclaimer,
 * must be included in all copies of the Software, in whole or in part, and
 * all derivative works of the Software, unless such copies or derivative
 * works are solely in the form of machine-executable object code generated by
 * a source language processor.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
 * SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
 * FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */
 #endregion

using System;
using System.Linq;
using EmberLib;
using EmberLib.Glow;
using EmberPlusProviderClassLib.Model;
using EmberPlusProviderClassLib.Model.Parameters;

namespace EmberPlusProviderClassLib
{
    public class Dispatcher : IElementVisitor<Dispatcher.ElementToGlowOptions, GlowContainer>
    {
        public Element Root { get; set; }

        #region GlowRootReady Event
        public class GlowRootReadyArgs : EventArgs
        {
            public GlowContainer Root { get; private set; }
            public Client SourceClient { get; private set; }
            public Matrix Matrix { get; set; }

            public GlowRootReadyArgs(GlowContainer root, Client sourceClient)
            {
                Root = root;
                SourceClient = sourceClient;
            }
        }

        public event EventHandler<GlowRootReadyArgs> GlowRootReady;

        protected virtual void OnGlowRootReady(GlowRootReadyArgs oArgs)
        {
            if (GlowRootReady != null)
                GlowRootReady(this, oArgs);
        }
        #endregion

        public void DispatchGlow(GlowContainer glow, Client source)
        {
            var walker = new Walker(this, source);

            walker.Walk(glow);
        }

        public void NotifyParameterValueChanged(ParameterBase parameter)
        {
            var options = new ElementToGlowOptions
            {
                DirFieldMask = GlowFieldFlags.Value
            };

            var glowParam = ElementToGlow(parameter, options);

            var glow = GlowRootElementCollection.CreateRoot();
            glow.Insert(glowParam);

            OnGlowRootReady(new GlowRootReadyArgs(glow, null));
        }

        public void NotifyParameterValueChanged(int[] parameterPath, GlowValue value)
        {
            var glowParam = new GlowQualifiedParameter(parameterPath)
            {
                Value = value,
            };

            var glow = GlowRootElementCollection.CreateRoot();
            glow.Insert(glowParam);

            OnGlowRootReady(new GlowRootReadyArgs(glow, null));
        }

        public void NotifyMatrixConnection(Matrix matrix, Signal target, object state)
        {
            var glow = GlowRootElementCollection.CreateRoot();
            var glowMatrix = new GlowQualifiedMatrix(matrix.Path);
            var glowConnection = new GlowConnection(target.Number)
            {
                Sources = target.ConnectedSources.Select(signal => signal.Number).ToArray(),
                Disposition = GlowConnectionDisposition.Modified,
            };

            glowMatrix.EnsureConnections().Insert(glowConnection);

            glow.Insert(glowMatrix);

            OnGlowRootReady(new GlowRootReadyArgs(glow, null) { Matrix = matrix });
        }

        #region Implementation
        GlowMatrixBase MatrixToGlow(Matrix matrix, ElementToGlowOptions options)
        {
            var dirFieldMask = options.DirFieldMask;
            var glow = new GlowQualifiedMatrix(matrix.Path)
            {
                Identifier = matrix.Identifier,
                TargetCount = matrix.TargetCount,
                SourceCount = matrix.SourceCount,
            };

            if (dirFieldMask.HasBits(GlowFieldFlags.Description)
            && String.IsNullOrEmpty(matrix.Description) == false)
                glow.Description = matrix.Description;

            if (matrix.LabelsNode != null
            && dirFieldMask == GlowFieldFlags.All)
            {
                var labels = new EmberSequence(GlowTags.MatrixContents.Labels);
                labels.Insert(new GlowLabel { BasePath = matrix.LabelsNode.Path, Description = "Primary" });
                glow.Labels = labels;
            }

            if (dirFieldMask.HasBits(GlowFieldFlags.Connections)
            && options.IsCompleteMatrixEnquired)
            {
                var glowConnections = glow.EnsureConnections();

                foreach (var signal in matrix.Targets)
                {
                    var glowConnection = new GlowConnection(signal.Number);

                    if (signal.ConnectedSources.Any())
                        glowConnection.Sources = signal.ConnectedSources.Select(source => source.Number).ToArray();

                    glowConnections.Insert(glowConnection);
                }
            }

            if ((dirFieldMask == GlowFieldFlags.All)
            && String.IsNullOrEmpty(matrix.SchemaIdentifier) == false)
                glow.SchemaIdentifiers = matrix.SchemaIdentifier;

            return glow;
        }

        GlowContainer ElementToGlow(Element element, ElementToGlowOptions options)
        {
            return element.Accept(this, options);
        }
        #endregion

        #region Class Walker
        class Walker : GlowWalker
        {
            public Walker(Dispatcher dispatcher, Client source)
            {
                _dispatcher = dispatcher;
                _source = source;
            }

            protected override void OnCommand(GlowCommand glow, int[] path)
            {
                IDynamicPathHandler dynamicPathHandler;
                var parent = _dispatcher.Root.ResolveChild(path, out dynamicPathHandler);

                if (parent != null)
                {
                    if (glow.Number == GlowCommandType.GetDirectory)
                    {
                        var glowRoot = GlowRootElementCollection.CreateRoot();
                        var options = new ElementToGlowOptions { DirFieldMask = glow.DirFieldMask ?? GlowFieldFlags.All };

                        var visitor = new InlineElementVisitor(
                            node =>
                            {
                                // "dir" in node
                                if (node.ChildrenCount == 0)
                                {
                                    glowRoot.Insert(new GlowQualifiedNode(node.Path));
                                }
                                else
                                {
                                    var glowChildren = from element in node.Children select _dispatcher.ElementToGlow(element, options);
                                    foreach (var glowChild in glowChildren)
                                        glowRoot.Insert(glowChild);
                                }
                            },
                            parameter =>
                            {
                                // "dir" in parameter
                                glowRoot.Insert(_dispatcher.ElementToGlow(parameter, options));
                            },
                            matrix =>
                            {
                                // "dir" in matrix
                                options.IsCompleteMatrixEnquired = true;
                                glowRoot.Insert(_dispatcher.ElementToGlow(matrix, options));
                                _source.SubscribeToMatrix(matrix, subscribe: true);
                            },
                            function =>
                            {
                                // "dir" in function
                                glowRoot.Insert(_dispatcher.ElementToGlow(function, options));
                            });

                        parent.Accept(visitor, null); // run inline visitor against parent

                        _source.Write(glowRoot);
                    }
                    else if (glow.Number == GlowCommandType.Unsubscribe)
                    {
                        var visitor = new InlineElementVisitor(onMatrix: matrix => _source.SubscribeToMatrix(matrix, subscribe: false));
                        parent.Accept(visitor, null); // run inline visitor against parent
                    }
                    else if (glow.Number == GlowCommandType.Invoke)
                    {
                        var visitor = new InlineElementVisitor(
                            onFunction: async function =>
                            {
                                var invocation = glow.Invocation;
                                var invocationResult = null as GlowInvocationResult;

                                try
                                {
                                    invocationResult = await function.Invoke(invocation);
                                }
                                catch
                                {
                                    if (invocation != null && invocation.InvocationId != null)
                                    {
                                        invocationResult = GlowInvocationResult.CreateRoot(invocation.InvocationId.Value);
                                        invocationResult.Success = false;
                                    }
                                }

                                if (invocationResult != null)
                                    _source.Write(invocationResult);
                            });

                        parent.Accept(visitor, null); // run inline visitor against parent
                    }
                }
                else
                {
                    if (dynamicPathHandler != null)
                        dynamicPathHandler.HandleCommand(glow, path, _source);
                }
            }

            protected override void OnParameter(GlowParameterBase glow, int[] path)
            {
                IDynamicPathHandler dynamicPathHandler;
                var parameter = _dispatcher.Root.ResolveChild(path, out dynamicPathHandler) as ParameterBase;

                if (parameter != null)
                {
                    var glowValue = glow.Value;

                    if (glowValue != null)
                    {
                        switch (glowValue.Type)
                        {
                            case GlowParameterType.Boolean:
                                {
                                    var booleanParameter = parameter as BooleanParameter;

                                    if (booleanParameter != null)
                                        booleanParameter.Value = glowValue.Boolean;

                                    break;
                                }

                            case GlowParameterType.Integer:
                                {
                                    var integerParameter = parameter as IntegerParameter;

                                    if (integerParameter != null)
                                        integerParameter.Value = glowValue.Integer;

                                    break;
                                }

                            case GlowParameterType.String:
                                {
                                    var stringParameter = parameter as StringParameter;

                                    if (stringParameter != null)
                                        stringParameter.Value = glowValue.String;

                                    break;
                                }
                        }
                    }
                }
                else
                {
                    if (dynamicPathHandler != null)
                        dynamicPathHandler.HandleParameter(glow, path, _source);
                }
            }

            protected override void OnMatrix(GlowMatrixBase glow, int[] path)
            {
                IDynamicPathHandler dummy;
                var matrix = _dispatcher.Root.ResolveChild(path, out dummy) as Matrix;

                if (matrix != null)
                {
                    var connections = glow.Connections;

                    if (connections != null)
                    {
                        foreach (var connection in glow.TypedConnections)
                        {
                            var target = matrix.GetTarget(connection.Target);

                            if (target != null)
                            {
                                var glowSources = connection.Sources;
                                var sources = glowSources != null
                                              ? from sourceNumber in glowSources
                                                let source = matrix.GetSource(sourceNumber)
                                                where source != null
                                                select source
                                              : Enumerable.Empty<Signal>();

                                var operation = connection.Operation != null
                                                ? (ConnectOperation)connection.Operation.Value
                                                : ConnectOperation.Absolute;

                                matrix.Connect(target, sources, _source, operation);
                            }
                        }
                    }
                }
            }

            protected override void OnNode(GlowNodeBase glow, int[] path)
            {
            }

            protected override void OnStreamEntry(GlowStreamEntry glow)
            {
            }

            protected override void OnFunction(GlowFunctionBase glow, int[] path)
            {
            }

            protected override void OnInvocationResult(GlowInvocationResult glow)
            {
            }

            protected override void OnTemplate(GlowTemplateBase glow, int[] path)
            {
            }

            #region Implementation
            Dispatcher _dispatcher;
            Client _source;
            #endregion
        }
        #endregion

        #region IElementVisitor<ElementToGlowOptions,GlowContainer> Members
        class ElementToGlowOptions
        {
            public int DirFieldMask { get; set; }
            public bool IsCompleteMatrixEnquired { get; set; }
        }

        GlowContainer IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(Node element, ElementToGlowOptions state)
        {
            var glow = new GlowQualifiedNode(element.Path);
            var dirFieldMask = state.DirFieldMask;

            if (dirFieldMask.HasBits(GlowFieldFlags.Identifier))
                glow.Identifier = element.Identifier;

            if ((dirFieldMask.HasBits(GlowFieldFlags.Description))
                && String.IsNullOrEmpty(element.Description) == false)
                glow.Description = element.Description;

            if ((dirFieldMask == GlowFieldFlags.All)
                && String.IsNullOrEmpty(element.SchemaIdentifier) == false)
                glow.SchemaIdentifiers = element.SchemaIdentifier;

            return glow;
        }

        GlowContainer Model.IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(BooleanParameter element, ElementToGlowOptions state)
        {
            var glow = new GlowQualifiedParameter(element.Path);
            var dirFieldMask = state.DirFieldMask;

            if (dirFieldMask.HasBits(GlowFieldFlags.Identifier))
                glow.Identifier = element.Identifier;

            if (dirFieldMask.HasBits(GlowFieldFlags.Description)
                && String.IsNullOrEmpty(element.Description) == false)
                glow.Description = element.Description;

            if (dirFieldMask.HasBits(GlowFieldFlags.Value))
                glow.Value = new GlowValue(element.Value);

            if (dirFieldMask == GlowFieldFlags.All)
            {
                if (element.IsWriteable)
                    glow.Access = GlowAccess.ReadWrite;
            }

            if ((dirFieldMask == GlowFieldFlags.All)
                && String.IsNullOrEmpty(element.SchemaIdentifier) == false)
                glow.SchemaIdentifiers = element.SchemaIdentifier;

            return glow;
        }

        GlowContainer IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(IntegerParameter element, ElementToGlowOptions state)
        {
            var glow = new GlowQualifiedParameter(element.Path);
            var dirFieldMask = state.DirFieldMask;

            if (dirFieldMask.HasBits(GlowFieldFlags.Identifier))
                glow.Identifier = element.Identifier;

            if (dirFieldMask.HasBits(GlowFieldFlags.Description)
                && String.IsNullOrEmpty(element.Description) == false)
                glow.Description = element.Description;

            if (dirFieldMask.HasBits(GlowFieldFlags.Value))
                glow.Value = new GlowValue(element.Value);

            if (dirFieldMask == GlowFieldFlags.All)
            {
                glow.Minimum = new GlowMinMax(element.Minimum);
                glow.Maximum = new GlowMinMax(element.Maximum);

                if (element.IsWriteable)
                    glow.Access = GlowAccess.ReadWrite;
            }

            if ((dirFieldMask == GlowFieldFlags.All)
            && String.IsNullOrEmpty(element.SchemaIdentifier) == false)
                glow.SchemaIdentifiers = element.SchemaIdentifier;

            return glow;
        }

        GlowContainer IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(StringParameter element, ElementToGlowOptions state)
        {
            var glow = new GlowQualifiedParameter(element.Path);
            var dirFieldMask = state.DirFieldMask;

            if (dirFieldMask.HasBits(GlowFieldFlags.Identifier))
                glow.Identifier = element.Identifier;

            if (dirFieldMask.HasBits(GlowFieldFlags.Description)
                && String.IsNullOrEmpty(element.Description) == false)
                glow.Description = element.Description;

            if (dirFieldMask.HasBits(GlowFieldFlags.Value))
                glow.Value = new GlowValue(element.Value);

            if (dirFieldMask == GlowFieldFlags.All)
            {
                if (element.IsWriteable)
                    glow.Access = GlowAccess.ReadWrite;
            }

            if ((dirFieldMask == GlowFieldFlags.All)
                && String.IsNullOrEmpty(element.SchemaIdentifier) == false)
                glow.SchemaIdentifiers = element.SchemaIdentifier;

            return glow;
        }

        GlowContainer IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(OneToNMatrix element, ElementToGlowOptions state)
        {
            var glow = MatrixToGlow(element, state);

            if (state.DirFieldMask == GlowFieldFlags.All
                && state.IsCompleteMatrixEnquired)
            {
                if (element.Targets.Count() < element.TargetCount)
                {
                    var glowTargets = glow.EnsureTargets();

                    foreach (var signal in element.Targets)
                        glowTargets.Insert(new GlowTarget(signal.Number));
                }

                if (element.Sources.Count() < element.SourceCount)
                {
                    var glowSources = glow.EnsureSources();

                    foreach (var signal in element.Sources)
                        glowSources.Insert(new GlowSource(signal.Number));
                }
            }

            return glow;
        }

        GlowContainer IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(NToNMatrix element, ElementToGlowOptions state)
        {
            var glow = MatrixToGlow(element, state);

            if (state.DirFieldMask == GlowFieldFlags.All)
            {
                glow.AddressingMode = GlowMatrixAddressingMode.NonLinear;
                glow.MatrixType = GlowMatrixType.NToN;

                if (element.ParametersNode != null)
                    glow.ParametersLocation = new GlowParametersLocation(element.ParametersNode.Path);

                if (state.IsCompleteMatrixEnquired)
                {
                    var glowTargets = glow.EnsureTargets();
                    var glowSources = glow.EnsureSources();

                    foreach (var signal in element.Targets)
                        glowTargets.Insert(new GlowTarget(signal.Number));

                    foreach (var signal in element.Sources)
                        glowSources.Insert(new GlowSource(signal.Number));
                }
            }

            return glow;
        }

        GlowContainer IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(DynamicMatrix element, ElementToGlowOptions state)
        {
            var glow = MatrixToGlow(element, state);

            if (state.DirFieldMask == GlowFieldFlags.All)
            {
                glow.MatrixType = GlowMatrixType.NToN;
                glow.ParametersLocation = new GlowParametersLocation(element.ParametersSubIdentifier);
                glow.GainParameterNumber = 1;
            }

            return glow;
        }

        GlowContainer IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(OneToOneMatrix element, ElementToGlowOptions state)
        {
            var glow = MatrixToGlow(element, state);

            if (state.DirFieldMask == GlowFieldFlags.All)
            {
                glow.MatrixType = GlowMatrixType.OneToOne;
            }

            return glow;
        }

        GlowContainer IElementVisitor<ElementToGlowOptions, GlowContainer>.Visit(Function element, ElementToGlowOptions state)
        {
            var glow = new GlowQualifiedFunction(element.Path);
            var dirFieldMask = state.DirFieldMask;

            if (dirFieldMask.HasBits(GlowFieldFlags.Identifier))
                glow.Identifier = element.Identifier;

            if (dirFieldMask.HasBits(GlowFieldFlags.Description)
                && String.IsNullOrEmpty(element.Description) == false)
                glow.Description = element.Description;

            if (dirFieldMask == GlowFieldFlags.All)
            {
                if (element.Arguments != null)
                {
                    var tupleItemDescs = from tuple in element.Arguments
                                         select new GlowTupleItemDescription(tuple.Item2) { Name = tuple.Item1 };
                    var arguments = glow.EnsureArguments();

                    foreach (var tupleItemDesc in tupleItemDescs)
                        arguments.Insert(tupleItemDesc);
                }

                if (element.Result != null)
                {
                    var tupleItemDescs = from tuple in element.Result
                                         select new GlowTupleItemDescription(tuple.Item2) { Name = tuple.Item1 };
                    var result = glow.EnsureResult();

                    foreach (var tupleItemDesc in tupleItemDescs)
                        result.Insert(tupleItemDesc);
                }
            }

            return glow;
        }
        #endregion
    }
}
