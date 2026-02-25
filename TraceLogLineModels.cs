namespace ZXTL
{
    public enum TraceLogEventKind
    {
        LoggedButNotExecuted,         // 'E'
        InterruptJump0,               // '0'
        InterruptJump1,               // '1'
        InterruptJump2,               // '2'
        InterruptJumpN,               // 'N'
        DjnzBranch,                   // 'L'
        JumpOrBranch,                 // '*'
        FrameStart,                   // 'F'
        HaltOrLdirInternal,           // 'H'
        RegularFlowInterruptsEnabled, // ' '
        RegularFlowInterruptsDisabled // '!'
    }

    public enum TraceLogOrderField
    {
        Unknown = 0,

        // Registers / flags
        PC,
        SP,
        A,
        F,
        B,
        C,
        D,
        E,
        H,
        L,
        AF,
        BC,
        DE,
        HL,
        IX,
        IY,
        IR,
        AFx,
        BCx,
        DEx,
        HLx,
        WZ,
        IM,
        IFF1,
        IFF2,

        // Ports
        Port7FFD,
        Port1FFD,
        PortFE,

        // Execution / emulator-specific
        Cycle,
        OpcodeValue,
        OpcodeAscii,
        Disassembly,
        Event
    }

    public sealed class TraceLogOrderDefinition
    {
        public string RawText { get; set; } = string.Empty;
        public List<TraceLogOrderFieldSpec> Fields { get; } = new();

        public void Clear()
        {
            RawText = string.Empty;
            Fields.Clear();
        }
    }

    public sealed class TraceLogOrderFieldSpec
    {
        public TraceLogOrderField Field { get; set; } = TraceLogOrderField.Unknown;
        public string RawToken { get; set; } = string.Empty;
    }

    public sealed class TraceLogLineData
    {
        public TraceLogRegisters Registers { get; } = new();
        public TraceLogPorts Ports { get; } = new();
        public TraceLogExecutionInfo Execution { get; } = new();

        // Convenience aliases for the access shape you want.
        public TraceLogEventKind? Event { get; set; }
        public char? EventCode { get; set; }

        public byte? Port7FFD
        {
            get => Ports.Port7FFD;
            set => Ports.Port7FFD = value;
        }

        public byte? Port1FFD
        {
            get => Ports.Port1FFD;
            set => Ports.Port1FFD = value;
        }

        public byte? PortFE
        {
            get => Ports.PortFE;
            set => Ports.PortFE = value;
        }

        public void Clear()
        {
            Registers.Clear();
            Ports.Clear();
            Execution.Clear();
            Event = null;
            EventCode = null;
        }
    }

    public sealed class TraceLogRegisters
    {
        // 8-bit registers
        public byte? A { get; set; }
        public byte? F { get; set; }
        public byte? B { get; set; }
        public byte? C { get; set; }
        public byte? D { get; set; }
        public byte? E { get; set; }
        public byte? H { get; set; }
        public byte? L { get; set; }

        // 16-bit registers / pairs
        public ushort? PC { get; set; }
        public ushort? SP { get; set; }
        public ushort? AF { get; set; }
        public ushort? BC { get; set; }
        public ushort? DE { get; set; }
        public ushort? HL { get; set; }
        public ushort? IX { get; set; }
        public ushort? IY { get; set; }
        public ushort? IR { get; set; }
        public ushort? AFx { get; set; }
        public ushort? BCx { get; set; }
        public ushort? DEx { get; set; }
        public ushort? HLx { get; set; }
        public ushort? WZ { get; set; }

        // Interrupt mode / flip-flops
        public byte? IM { get; set; }
        public bool? IFF1 { get; set; }
        public bool? IFF2 { get; set; }

        public void Clear()
        {
            A = F = B = C = D = E = H = L = null;
            PC = SP = AF = BC = DE = HL = IX = IY = IR = AFx = BCx = DEx = HLx = WZ = null;
            IM = null;
            IFF1 = null;
            IFF2 = null;
        }
    }

    public sealed class TraceLogPorts
    {
        public byte? Port7FFD { get; set; }
        public byte? Port1FFD { get; set; }
        public byte? PortFE { get; set; }

        public void Clear()
        {
            Port7FFD = null;
            Port1FFD = null;
            PortFE = null;
        }
    }

    public sealed class TraceLogExecutionInfo
    {
        // T-state / cycle counter
        public long? Cycle { get; set; }

        // Executed opcode (up to 4 bytes), its ASCII view and disassembly text.
        public TraceLogOpcodeInfo Opcode { get; } = new();
        public string? Disassembly { get; set; }

        public void Clear()
        {
            Cycle = null;
            Opcode.Clear();
            Disassembly = null;
        }
    }

    public sealed class TraceLogOpcodeInfo
    {
        // Normalized packed numeric opcode value when available.
        public uint? Value { get; set; }

        // Raw bytes (many emulators emit 1-4 bytes depending on instruction/prefixes).
        public byte? Byte0 { get; set; }
        public byte? Byte1 { get; set; }
        public byte? Byte2 { get; set; }
        public byte? Byte3 { get; set; }

        // 4-char ASCII representation shown by some emulators.
        public string? Ascii { get; set; }

        public void Clear()
        {
            Value = null;
            Byte0 = null;
            Byte1 = null;
            Byte2 = null;
            Byte3 = null;
            Ascii = null;
        }
    }
}
