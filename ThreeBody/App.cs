using CodePlayground;
using CodePlayground.Graphics;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ThreeBody
{
    internal readonly struct PipelineSpec : IPipelineSpecification
    {
        public const PipelineFrontFace Winding = PipelineFrontFace.Clockwise;

        public PipelineBlendMode BlendMode => PipelineBlendMode.Default;
        public PipelineFrontFace FrontFace => Winding;
        public bool EnableDepthTesting => true;
        public bool DisableCulling => true;
    }

    [ApplicationTitle("Three body")]
    public sealed class App : GraphicsApplication
    {
        public static int Main(string[] args) => RunApplication<App>(args);

        public App()
        {
            Load += OnLoad;
            Closing += OnClose;
            Update += OnUpdate;
            Render += OnRender;
            FramebufferResize += OnFramebufferResize;
        }

        // Must pass in counter-clockwise order!
        // 1-0
        // |/
        // 2
        private static bool AddTriangle(IList<uint> indices, PipelineFrontFace winding, IReadOnlyList<uint> face)
        {
            using var addTriangleEvent = Profiler.Event();
            if (face.Count != 4)
            {
                return false;
            }

            var order = winding switch
            {
                PipelineFrontFace.Clockwise => new int[]
                {
                    1, 0, 2
                },
                PipelineFrontFace.CounterClockwise => new int[]
                {
                    0, 1, 2,
                },
                _ => Array.Empty<int>()
            };

            if (order.Length == 0)
            {
                return false;
            }

            foreach (int index in order)
            {
                indices.Add(face[index]);
            }

            return true;
        }

        // Must pass in counter-clockwise order!
        // 1-0
        // | |
        // 2-3
        private static bool AddQuad(IList<uint> indices, PipelineFrontFace winding, IReadOnlyList<uint> face)
        {
            using var addQuadEvent = Profiler.Event();
            if (face.Count != 4)
            {
                return false;
            }

            var order = winding switch
            {
                PipelineFrontFace.Clockwise => new int[]
                {
                    1, 0, 2,
                    0, 3, 2
                },
                PipelineFrontFace.CounterClockwise => new int[]
                {
                    0, 1, 2,
                    0, 2, 3
                },
                _ => Array.Empty<int>()
            };

            if (order.Length == 0)
            {
                return false;
            }

            foreach (int index in order)
            {
                indices.Add(face[index]);
            }

            return true;
        }

        // https://danielsieger.com/blog/2021/03/27/generating-spheres.html
        private static void GenerateSphere(int slices, int stacks, IList<Vector3> vertices, IList<uint> indices)
        {
            using var generateSphereEvent = Profiler.Event();

            vertices.Clear();
            indices.Clear();

            vertices.Add(Vector3.UnitY);
            for (int i = 0; i < stacks - 1; i++)
            {
                float phi = MathF.PI * (i + 1) / stacks;
                for (int j = 0; j < slices; j++)
                {
                    float theta = 2f * MathF.PI * j / slices;
                    vertices.Add(new Vector3
                    {
                        X = MathF.Sin(phi) * MathF.Cos(theta),
                        Y = MathF.Cos(phi),
                        Z = MathF.Sin(phi) * MathF.Sin(theta)
                    });
                }
            }

            int lastVertex = vertices.Count;
            vertices.Add(-Vector3.UnitY);

            for (int i = 0; i < slices; i++)
            {
                AddTriangle(indices, PipelineSpec.Winding, new uint[]
                {
                    0,
                    (uint)(i + 1),
                    (uint)((i + 1) % slices + 1)
                });

                AddTriangle(indices, PipelineSpec.Winding, new uint[]
                {
                    (uint)lastVertex,
                    (uint)(i + slices * (stacks - 2) + 1),
                    (uint)((i + 1) % slices + slices * (stacks - 2) + 1)
                });
            }

            for (int j = 0; j < stacks - 2; j++)
            {
                int j0 = j * slices + 1;
                int j1 = (j + 1) * slices + 1;

                for (int i = 0; i < slices; i++)
                {
                    AddQuad(indices, PipelineSpec.Winding, new uint[]
                    {
                        (uint)(j0 + i),
                        (uint)(j0 + (i + 1) % slices),
                        (uint)(j1 + (i + 1) % slices),
                        (uint)(j1 + i)
                    });
                }
            }
        }

        private void OnFramebufferResize(Vector2D<int> size)
        {
            mViewSize = new Vector2(size.X, size.Y);
        }

        private void OnLoad()
        {
            using var loadEvent = Profiler.Event();

            InitializeGraphics();
            AddBodies();
        }

        private struct BodyDesc
        {
            public Vector3 Color, Position, Velocity;
            public float Radius, Density;
            public bool IsSolid;
        }

        // https://stackoverflow.com/questions/1335426/is-there-a-built-in-c-net-system-api-for-hsv-to-rgb
        private static Vector3 HSVToRGB(Vector3 hsv)
        {
            int hi = Convert.ToInt32(float.Floor(hsv.X / 60)) % 6;
            double f = hsv.X / 60 - float.Floor(hsv.X / 60);

            float value = hsv.Z * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - hsv.Y));
            int q = Convert.ToInt32(value * (1 - f * hsv.Y));
            int t = Convert.ToInt32(value * (1 - (1 - f) * hsv.Y));

            return hi switch
            {
                0 => new Vector3(v, t, p),
                1 => new Vector3(q, v, p),
                2 => new Vector3(p, v, t),
                3 => new Vector3(p, q, v),
                4 => new Vector3(t, p, v),
                _ => new Vector3(v, p, q)
            } / 255f;
        }

        private static void AddBodies()
        {
            using var addBodiesEvent = Profiler.Event();
            var registry = Physics.Registry;

            const float bodyRadius = 1f;
            const float bodyDensity = 3f / (4f * MathF.PI);
            const float earthDensity = 1e+10f;
            const float earthRadius = 10f;
            const float geosynchronousOrbitRadius = 10f;
            const float orbitRadius = earthRadius + geosynchronousOrbitRadius;
            const int bodyCount = 15;

            var bodies = new List<BodyDesc>
            {
                new BodyDesc
                {
                    Color = Vector3.One,
                    Position = Vector3.Zero,
                    Velocity = Vector3.Zero,
                    Radius = earthRadius,
                    Density = earthDensity,
                    IsSolid = true
                },
            };

            float earthMass = Physics.SphereVolume(earthRadius) * earthDensity;
            float orbitVelocity = MathF.Sqrt(Physics.G * earthMass / orbitRadius);

            for (int i = 0; i < bodyCount; i++)
            {
                float angle = 2f * MathF.PI * i / bodyCount;
                bodies.Add(new BodyDesc
                {
                    Color = HSVToRGB(new Vector3(angle * 180f / MathF.PI, 1f, 1f)),
                    Position = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * orbitRadius,
                    Velocity = new Vector3(-MathF.Sin(angle), MathF.Cos(angle), 0f) * orbitVelocity * ((float)i / bodyCount),
                    Radius = bodyRadius,
                    Density = bodyDensity,
                    IsSolid = true
                });
            }

            foreach (var desc in bodies)
            {
                ulong entity = registry.New();
                ref var body = ref registry.Add<Body>(entity).Value;

                body.Position = desc.Position;
                body.Velocity = desc.Velocity;
                body.Acceleration = Vector3.Zero;
                body.Density = desc.Density;
                body.Radius = desc.Radius;
                body.Color = new Vector4(desc.Color, 1f);
                body.IsSolid = desc.IsSolid;
            }
        }

        private void InitializeGraphics()
        {
            using var initGraphicsEvent = Profiler.Event();

            var viewSize = RootView!.FramebufferSize;
            mViewSize = new Vector2(viewSize.X, viewSize.Y);

            var context = CreateGraphicsContext();
            context.Swapchain!.VSync = true;

            mLibrary = new ShaderLibrary(context, GetType().Assembly);
            mRenderer = context.CreateRenderer();
            mPipeline = mLibrary.LoadPipeline<InstancedShader>(new PipelineDescription
            {
                FrameCount = context.Swapchain.FrameCount,
                Type = PipelineType.Graphics,
                RenderTarget = context.Swapchain?.RenderTarget,
                Specification = new PipelineSpec()
            });

            var reflectionView = mPipeline.ReflectionView;
            mCameraBuffer = new UniformBuffer(nameof(InstancedShader.Camera), reflectionView, DeviceBufferUsage.Uniform);
            mInstanceBuffer = new UniformBuffer(nameof(InstancedShader.Instances), reflectionView, DeviceBufferUsage.Uniform);

            mCameraBuffer.Bind(mPipeline);
            mInstanceBuffer.Bind(mPipeline);

            const int slices = 100;
            const int stacks = 50;

            var vertices = new List<Vector3>();
            var indices = new List<uint>();
            GenerateSphere(slices, stacks, vertices, indices);

            int vertexBufferSize = vertices.Count * Marshal.SizeOf<Vector3>();
            int indexBufferSize = indices.Count * Marshal.SizeOf<uint>();

            var vertexStaging = context.CreateDeviceBuffer(DeviceBufferUsage.Staging, vertexBufferSize);
            var indexStaging = context.CreateDeviceBuffer(DeviceBufferUsage.Staging, indexBufferSize);

            mVertexBuffer = context.CreateDeviceBuffer(DeviceBufferUsage.Vertex, vertexBufferSize);
            mIndexBuffer = context.CreateDeviceBuffer(DeviceBufferUsage.Index, indexBufferSize);
            mIndexCount = indices.Count;

            vertexStaging.CopyFromCPU(vertices.ToArray());
            indexStaging.CopyFromCPU(indices.ToArray());

            var transferQueue = context.Device.GetQueue(CommandQueueFlags.Transfer);
            var commandList = transferQueue.Release();

            mSemaphore = context.CreateSemaphore();
            mSemaphoreTriggered = true;

            commandList.AddSemaphore(mSemaphore, SemaphoreUsage.Signal);
            commandList.PushStagingObject(vertexStaging);
            commandList.PushStagingObject(indexStaging);

            commandList.Begin();
            vertexStaging.CopyBuffers(commandList, mVertexBuffer, vertexBufferSize);
            indexStaging.CopyBuffers(commandList, mIndexBuffer, indexBufferSize);

            commandList.End();
            transferQueue.Submit(commandList);
        }

        private void OnClose()
        {
            using var closeEvent = Profiler.Event();
            Physics.Registry.Clear();

            var context = GraphicsContext;
            if (context != null)
            {
                context.Device.ClearQueues();

                mVertexBuffer?.Dispose();
                mIndexBuffer?.Dispose();
                mCameraBuffer?.Dispose();
                mInstanceBuffer?.Dispose();
                mPipeline?.Dispose();
                mSemaphore?.Dispose();
                mLibrary?.Dispose();

                context.Dispose();
            }
        }

        private void UpdateBuffers()
        {
            using var updateBuffersEvent = Profiler.Event();
            var math = new MatrixMath(GraphicsContext!);

            if (mCameraBuffer is not null)
            {
                var cameraPosition = Vector3.UnitZ * -100f;
                var cameraDirection = Vector3.UnitZ;
                float aspectRatio = mViewSize.X / mViewSize.Y;

                var view = math.LookAt(cameraPosition, cameraPosition + cameraDirection, Vector3.UnitY);
                var projection = math.Perspective(MathF.PI / 4f, aspectRatio, 0.1f, 100f);

                mCameraBuffer.Set(nameof(CameraData.Projection), math.TranslateMatrix(projection));
                mCameraBuffer.Set(nameof(CameraData.View), math.TranslateMatrix(view));
            }

            if (mInstanceBuffer is not null)
            {
                mInstanceCount = 0;

                var registry = Physics.Registry;
                foreach (ulong entity in registry.View(typeof(Body)))
                {
                    ref var body = ref registry.Get<Body>(entity).Value;

                    var model = Matrix4x4.Identity;
                    model = MatrixMath.Scale(model, Vector3.One * body.Radius);
                    model = MatrixMath.Translate(model, body.Position);

                    int instance = mInstanceCount++;
                    var stub = $"{nameof(InstanceBufferData.Instances)}[{instance}].";

                    mInstanceBuffer.Set(stub + nameof(InstanceData.Model), math.TranslateMatrix(model));
                    mInstanceBuffer.Set(stub + nameof(InstanceData.Color), body.Color);
                }
            }
        }

        private void OnUpdate(double delta)
        {
            using var updateEvent = Profiler.Event();

            Physics.Update(1f / 60f);
            UpdateBuffers();
        }

        private void OnRender(FrameRenderInfo renderInfo)
        {
            using var renderEvent = Profiler.Event();
            if (renderInfo.CommandList is null || renderInfo.RenderTarget is null || renderInfo.Framebuffer is null)
            {
                return;
            }

            if (mSemaphoreTriggered)
            {
                renderInfo.CommandList.AddSemaphore(mSemaphore!, SemaphoreUsage.Wait);
                mSemaphoreTriggered = false;
            }

            renderInfo.RenderTarget.BeginRender(renderInfo.CommandList, renderInfo.Framebuffer, new Vector4(0f, 0f, 0f, 1f));
            mPipeline!.Bind(renderInfo.CommandList, renderInfo.CurrentImage);
            mVertexBuffer!.BindVertices(renderInfo.CommandList, 0);
            mIndexBuffer!.BindIndices(renderInfo.CommandList, DeviceBufferIndexType.UInt32);

            mRenderer!.RenderInstanced(renderInfo.CommandList, 0, mIndexCount, 0, mInstanceCount);
            renderInfo.RenderTarget.EndRender(renderInfo.CommandList);
        }

        private Vector2 mViewSize;

        private ShaderLibrary? mLibrary;
        private UniformBuffer? mCameraBuffer, mInstanceBuffer;
        private IDeviceBuffer? mVertexBuffer, mIndexBuffer;
        private IRenderer? mRenderer;
        private IPipeline? mPipeline;
        private int mIndexCount, mInstanceCount;

        private IDisposable? mSemaphore;
        private bool mSemaphoreTriggered;
    }
}