using CodePlayground;
using CodePlayground.Graphics;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ThreeBody
{
    public sealed class UniformBuffer : IDisposable
    {
        public UniformBuffer(string name, IReflectionView reflection, DeviceBufferUsage usage)
        {
            mDisposed = false;
            mName = name;

            int size = reflection.GetBufferSize(name);
            if (size <= 0)
            {
                throw new ArgumentException("Invalid buffer!");
            }

            if (usage is not DeviceBufferUsage.Uniform && usage is not DeviceBufferUsage.Storage)
            {
                throw new ArgumentException("Invalid usage!");
            }

            var context = ((GraphicsApplication)Application.Instance).GraphicsContext!;
            mBuffer = context.CreateDeviceBuffer(usage, size);
            mNode = reflection.GetResourceNode(name) ?? throw new InvalidOperationException("Failed to retrieve resource node!");
        }

        ~UniformBuffer()
        {
            if (!mDisposed)
            {
                Dispose(false);
                mDisposed = true;
            }
        }

        public void Dispose()
        {
            if (mDisposed)
            {
                return;
            }

            Dispose(true);
            mDisposed = true;
        }

        private void Dispose(bool disposing)
        {
            using var disposeEvent = Profiler.Event();
            if (disposing)
            {
                mBuffer.Dispose();
            }
        }

        private IReflectionNode? FindNode(string name)
        {
            using var findNodeEvent = Profiler.Event();

            var currentNode = mNode;
            foreach (var segment in name.Split('.'))
            {
                currentNode = currentNode.Find(segment);
                if (currentNode is null)
                {
                    break;
                }
            }

            return currentNode;
        }

        public bool Set<T>(string name, T data) where T : unmanaged
        {
            using var setEvent = Profiler.Event();

            var node = FindNode(name);
            if (node is null)
            {
                return false;
            }

            mBuffer.Map(span =>
            {
                node.Set(span, data);
            });

            return true;
        }

        public unsafe T Get<T>(string name) where T : unmanaged
        {
            using var getEvent = Profiler.Event();
            var node = FindNode(name);
            T data = default;

            if (node is not null)
            {
                mBuffer.Map(span =>
                {
                    void* dst = Unsafe.AsPointer(ref data);
                    var bytes = span[node.Offset..(node.Offset + Marshal.SizeOf<T>())].ToArray();
                    Marshal.Copy(bytes, 0, (nint)dst, bytes.Length);
                });
            }

            return data;
        }

        public void Bind(IPipeline pipeline)
        {
            pipeline.Bind(mBuffer, mName);
        }

        private bool mDisposed;
        private readonly IReflectionNode mNode;
        private readonly IDeviceBuffer mBuffer;
        private readonly string mName;
    }
}
