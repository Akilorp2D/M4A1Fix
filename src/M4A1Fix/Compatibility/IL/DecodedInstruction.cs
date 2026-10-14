using System.Reflection.Emit;

namespace M4A1Fix.Compatibility.IL
{
    internal sealed class DecodedInstruction
    {
        internal int Offset;
        internal OpCode OpCode;
        internal object Operand;
    }
}
