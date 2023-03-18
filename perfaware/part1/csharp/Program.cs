#pragma warning disable CS8625
class Program
{
    struct Instruction
    {
        public string Disassembly;
        public int Length;
    }

    private delegate Instruction Decoder(System.IO.FileStream fs, byte[] buffer);

    static byte[] buffer = new byte[4];
    const byte opcode_mov_rm_reg = 0x22;
    const byte opcode_mov_imm_rm = 0x63;
    const byte opcode_mov_imm_reg = 0x0b;

    static string [] registers = new string[16] 
    {
        "AL", "CL", "DL", "BL", "AH", "CH", "DH", "BH", // w = 0
        "AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI", // w = 1
    };

    static void Main(string[] args)
    {
        if(args.Length == 0)
            return;

        Console.WriteLine("bits 16\n");

        string path = args[0];
        Instruction instruction;
        var fs = System.IO.File.OpenRead(path);
        for(int i = 0; i < fs.Length;)
        {
            fs.Read(buffer, 0, 1);
            
            if(!GetDecoder(buffer[0], out Decoder decoder))
            {
                Console.WriteLine($"Unexpected byte at {fs.Position} {buffer[0].ToString("X4")}");
                break;
            }

            instruction = decoder(fs, buffer);
            Console.WriteLine(instruction.Disassembly);
            i += instruction.Length;
        }
        fs.Close();
    }

    private static bool GetDecoder(byte opcode, out Decoder decoder)
    {
        if((opcode >> 2) == opcode_mov_rm_reg)
        {
            decoder = mov_rm_reg;
            return true;
        }
        else if((opcode >> 1) == opcode_mov_imm_rm)
        {
            decoder = mov_imm_rm;
            return true;
        }
        else if((opcode >> 4) == opcode_mov_imm_reg)
        {
            decoder = mov_imm_reg;
            return true;
        }

        decoder = null;
        return false;
    }

    private static Instruction mov_rm_reg(System.IO.FileStream fs, byte[] buffer)
    {
        var opcode = buffer[0];
        byte d = (byte)(opcode & 0x02);
        byte w = (byte)(opcode & 0x01);

        int offset = 1;
        int length = 1;

        length += fs.Read(buffer, offset, 1);        
        byte mod = (byte)((buffer[1] & 0xc0) >> 6);
        byte reg = (byte)((buffer[1] & 0x38) >> 3);
        byte rm = (byte)(buffer[1] & 0x07);
        
        byte dest;
        byte source;

        if(d == 0)
        {
            dest = rm;
            source = reg;
        }
        else
        {
            dest = reg;
            source = rm;
        }

        var destString = string.Empty;
        var sourceString = string.Empty;
        
        switch(mod)
        {
            case 0x00:
            {
                destString = registers[dest + 8 * w];
                
                if((~rm & 0x04) == 0x04)
                {
                    string r1 = registers[11 + (rm & 0x02)];
                    string r2 = registers[14 + (rm & 0x01)];
                    sourceString = $"[{r1} + {r2}]";
                }
                else
                {
                    string r;
                    if((~rm & 0x02) == 0x02)
                    {
                        r = registers[14 + (rm & 0x01)];
                    }
                    else
                    {
                        r = registers[13 - (rm & 0x01) * 2];
                    }
                }
            }
            break;
            case 0x01:
            {

            }
            break;
            case 0x03:
            {
                destString = registers[dest + 8 * w];
                sourceString = registers[source + 8 * w];
            }
            break;
            default:
            throw new NotImplementedException($"{Convert.ToString(buffer[0], 2)} {Convert.ToString(buffer[1], 2)}");
        }
        
        return new Instruction
        {
            Disassembly = $"mov {destString}, {sourceString}",
            Length = length,
        };
    }

    private static Instruction mov_imm_rm(System.IO.FileStream fs, byte[] buffer)
    {
        var opcode = buffer[0];
        byte w = (byte)(opcode & 0x01);

        int length = 1;
        int offset = 1;

        length += fs.Read(buffer, offset++, 1);
        byte mod = (byte)((buffer[1] & 0xc0) >> 6);
        byte rm = (byte)(buffer[1] & 0x07);

        length += fs.Read(buffer, offset++, 1);
        length += fs.Read(buffer, offset++, 1);

        UInt16 data = 0x00;
        switch(w)
        {
            case 0x00:
            {
                length += fs.Read(buffer, offset++, 1);
                data = buffer[4];
            }
            break;
            case 0x01:
            {
                length += fs.Read(buffer, offset, 2);
                offset += 2;
                data |= buffer[4];
                data |= buffer[5];
            }
            break;
        }

        var destString = string.Empty;
        var sourceString = string.Empty;

        switch(mod)
        {
            case 0x03:
            {
                destString = registers[rm + 8 * w];
                sourceString = data.ToString();
            }
            break;
        }

        return new Instruction
        {
            Disassembly = $"mov {destString}, {sourceString}",
            Length = length,
        };
    }

    private static Instruction mov_imm_reg(System.IO.FileStream fs, byte[] buffer)
    {
        byte opcode = buffer[0];
        byte w = (byte)((opcode & 0x08) >> 3);
        byte reg = (byte)(opcode & 0x07);

        int length = 1;
        int offset = 1;

        ushort data = 0x00;
        switch(w)
        {
            case 0x00:
            {
                length += fs.Read(buffer, offset++, 1);
                data = buffer[1];
            }
            break;
            case 0x01:
            {
                length += fs.Read(buffer, offset, 2);
                offset += 2;
                data |= buffer[1];
                data |= (ushort)(buffer[2] << 8);
            }
            break;
        }

        string destString = registers[reg + 8 * w];
        string sourceString = data.ToString();

        return new Instruction
        {
            Disassembly = $"mov {destString}, {sourceString}",
            Length = length,
        };
    }
}