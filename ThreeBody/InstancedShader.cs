using CodePlayground.Graphics;
using CodePlayground.Graphics.Shaders;

namespace ThreeBody
{
    public struct VertexInput
    {
        [Layout(Location = 0)]
        public Vector3<float> Position;

        [ShaderVariable(ShaderVariableID.InstanceID)]
        public int Instance;
    }

    public struct FragmentInput
    {
        [Layout(Location = 0)]
        public Vector4<float> Color;
    }

    public struct VertexOutput
    {
        [ShaderVariable(ShaderVariableID.OutputPosition)]
        public Vector4<float> Position;

        public FragmentInput Data;
    }

    public struct InstanceData
    {
        public Matrix4x4<float> Model;
        public Vector4<float> Color;
    }

    public struct InstanceBufferData
    {
        [ArraySize(InstancedShader.MaxInstances)]
        public InstanceData[] Instances;
    }

    public struct CameraData
    {
        public Matrix4x4<float> Projection, View;
    }

    [CompiledShader]
    public class InstancedShader
    {
        public const int MaxInstances = 20;

        [Layout(Set = 0, Binding = 0)]
        public static InstanceBufferData Instances;

        [Layout(Set = 0, Binding = 1)]
        public static CameraData Camera;

        [ShaderEntrypoint(ShaderStage.Vertex)]
        public static VertexOutput VertexMain(VertexInput input)
        {
            var instance = Instances.Instances[input.Instance];
            return new VertexOutput
            {
                Position = Camera.Projection * Camera.View * instance.Model * new Vector4<float>(input.Position, 1f),
                Data = new FragmentInput
                {
                    Color = instance.Color
                }
            };
        }

        [ShaderEntrypoint(ShaderStage.Fragment)]
        [return: Layout(Location = 0)]
        public static Vector4<float> FragmentMain(FragmentInput input) => new Vector4<float>(1f, 0f, 0f, 1f);
    }
}
