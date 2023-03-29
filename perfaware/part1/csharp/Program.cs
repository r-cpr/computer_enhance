#pragma warning disable CS8625
using System.Text;

class Program
{
    struct Instruction
    {
        public string Disassembly;
        public int Length;
    }

    private delegate Instruction Decoder(System.IO.FileStream fs, byte[] buffer);

    static byte[] buffer = new byte[6];
    static StringBuilder encodingBuilder = new StringBuilder();
    const byte opcode_mov_rm_reg = 0x22;
    const byte opcode_mov_imm_rm = 0x63;
    const byte opcode_mov_imm_reg = 0x0b;

    const byte opcode_add_rm_reg = 0x00;
    const byte opcode_add_imm_rm = 0x80;
    const byte opcode_add_imm_acc = 0x04;

    const byte opcode_sub_rm_reg = 0x28;
    const byte opcode_sub_imm_rm = 0x80;
    const byte opcode_sub_imm_acc = 0x2c;

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
                Console.WriteLine($"Could not find decoder for byte at {fs.Position}: b{Convert.ToString(buffer[0], 2).PadLeft(8, '0')} 0x{Convert.ToString(buffer[0], 16)}");
                break;
            }

            encodingBuilder.Clear();
            try
            {
                instruction = decoder(fs, buffer);

                for(int j = 0; j < instruction.Length; j++)
                {
                    Console.Write(Convert.ToString(buffer[j], 2).PadLeft(8, '0'));
                    Console.Write(" ");
                }

                Console.WriteLine(instruction.Disassembly);
                i += instruction.Length;
            }
            catch(Exception e) 
            {
                Console.WriteLine($"Exception decoding instruction {Convert.ToString(buffer[0],2 )} {e}");
                break;
            }
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
        else if((opcode & 0xfc) == opcode_add_rm_reg)
        {
            decoder = add_rm_reg;
            return true;
        }
        else if((opcode & 0xfc) == opcode_add_imm_rm)
        {
            decoder = add_imm_rm;
            return true;
        }
        else if((opcode & 0xfe) == opcode_add_imm_acc)
        {
            decoder = add_imm_acc;
            return true;
        }
        else if((opcode & 0xfc) == opcode_sub_rm_reg)
        {
            decoder = sub_rm_reg;
            return true;
        }
        else if((opcode & 0xfc) == opcode_sub_imm_rm)
        {
            decoder = sub_imm_rm;
            return true;
        }
        else if((opcode & 0xfe) == opcode_sub_imm_acc)
        {
            decoder = sub_imm_acc;
            return true;
        }

        decoder = null;
        return false;
    }

    private static int get_memory_address_encoding(System.IO.FileStream fs, byte[] buffer, StringBuilder stringBuilder, byte mod, byte rm, int index)
    {
        stringBuilder.Append("[");

        if((~rm & 0x04) == 0x04)
        {
            string r1 = registers[11 + (rm & 0x02)];
            string r2 = registers[14 + (rm & 0x01)];
            stringBuilder.Append($"{r1} + {r2}");
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
            stringBuilder.Append(r);
        }

        ushort disp = 0;    
        switch(mod)
        {
            case 0x01:
            {
                int readCount = fs.Read(buffer, index, 1);
                disp = buffer[index];
                index += readCount;       
            }
            break;
            case 0x02:
            {
                int readCount = fs.Read(buffer, index, 2);
                disp = buffer[index];
                disp |= (ushort)(buffer[index+1] << 8);
                index += readCount;
            }
            break;
        }

        if(disp != 0)
            stringBuilder.Append($" + {disp}");

        stringBuilder.Append("]");
        return index;
    }

    private static int get_mod_reg_rm_encodings(System.IO.FileStream fs, byte[] buffer, out string dest, out string source)
    {
        var opcode = buffer[0];
        byte d = (byte)(opcode & 0x02);
        byte w = (byte)(opcode & 0x01);

        int length = 1;

        length += fs.Read(buffer, 1, 1);        
        byte mod = (byte)((buffer[1] & 0xc0) >> 6);
        byte reg = (byte)((buffer[1] & 0x38) >> 3);
        byte rm = (byte)(buffer[1] & 0x07);
        
        string regEncoding;
        string rmEncoding;

        switch(mod)
        {
            case 0x00:
            case 0x01:
            case 0x02:
            {
                // register to memory
                regEncoding = registers[reg + 8 * w];
                length = get_memory_address_encoding(fs, buffer, encodingBuilder, mod, rm, length);
                rmEncoding = encodingBuilder.ToString();
            }
            break;
            case 0x03: 
            {
                // register to register
                regEncoding = registers[reg + 8 * w];
                rmEncoding = registers[rm + 8 * w];
            }
            break;
            default:
            throw new Exception();
        }

        if(d == 0)
        {
            dest = rmEncoding;
            source = regEncoding;
        }
        else
        {
            dest = regEncoding;
            source = rmEncoding;
        }

        return length;
    }

    private static int get_imm_rm_encodings(System.IO.FileStream fs, byte[] buffer, bool signedExt, out string dest, out string source)
    {
        int index = 0;
        int readCount = 0;
        var opcode = buffer[index++];
        byte w = (byte)(opcode & 0x01);

        readCount = fs.Read(buffer, index, 1);
        byte mod = (byte)((buffer[index] & 0xc0) >> 6);
        byte rm = (byte)(buffer[index++] & 0x07);
        index += readCount;

        switch(mod)
        {
            case 0x00: // to memory
            {
                switch(w)
                {
                    case 0x00:
                        encodingBuilder.Append("byte ");
                        break;
                    case 0x01:
                        encodingBuilder.Append("word ");
                        break;
                }
                index = get_memory_address_encoding(fs, buffer, encodingBuilder, mod, rm, index);
                dest = encodingBuilder.ToString();
            }
            break;
            case 0x02: // to memory with 16 bit displacement
            {
                index = get_memory_address_encoding(fs, buffer, encodingBuilder, mod, rm, index);
                dest = encodingBuilder.ToString();
            }
            break;
            case 0x03: // to register
            {
                dest = registers[rm + 8 * w];
            }
            break;
            default:
            {
                // displacement
                readCount = fs.Read(buffer, index, 1);
                index += readCount;
                readCount = fs.Read(buffer, index, 1);
                index += readCount;
                throw new System.Exception(mod.ToString());
            }
        }

        // immediate data
        ushort data = 0x00;
        switch(w)
        {
            case 0x00:
            {
                readCount = fs.Read(buffer, index, 1);
                data = buffer[index];
                index += readCount;
            }
            break;
            case 0x01:
            {   
                if(!signedExt || (opcode & 0x02) == 0x00)
                {
                    readCount = fs.Read(buffer, index, 2);
                    data |= buffer[index];
                    data |= (ushort)(buffer[index + 1] << 8);
                    signedExt = false;
                    index += readCount;
                }
                else
                {
                    readCount = fs.Read(buffer, index, 1);
                    data |= buffer[index];
                    index += readCount;
                }
            }
            break;
        }

        if(signedExt)
            source = ((short)data).ToString();
        else
            source = data.ToString();

        return index;
    }

    private static int get_imm_reg_encodings(System.IO.FileStream fs, byte[] buffer, out string dest, out string source)
    {
        byte opcode = buffer[0];
        byte w = (byte)((opcode & 0x08) >> 3);
        byte reg = (byte)(opcode & 0x07);

        int index = read_data(fs, w, 1, out ushort data);

        dest = registers[reg + 8 * w];
        source = data.ToString();

        return index;
    }

    private static int read_data(System.IO.FileStream fs, byte w, int index, out ushort data)
    {
        data = 0x00;
        int readCount = 0;
        switch(w)
        {
            case 0x00:
            {
                readCount = fs.Read(buffer, index, 1);
                data = buffer[index];
            }
            break;
            case 0x01:
            {
                readCount = fs.Read(buffer, index, 2);
                data |= buffer[index];
                data |= (ushort)(buffer[index + 1] << 8);
            }
            break;
        }

        index += readCount;
        return index;
    }

    private static Instruction add_rm_reg(System.IO.FileStream fs, byte[] buffer)
    {
        int length = get_mod_reg_rm_encodings(fs, buffer, out string dest, out string source);

        return new Instruction
        {
            Disassembly = $"add {dest}, {source}",
            Length = length,
        };
    }

    private static Instruction add_imm_rm(System.IO.FileStream fs, byte[] buffer)
    {
        int length = get_imm_rm_encodings(fs, buffer, true, out string dest, out string source);

        return new Instruction
        {
            Disassembly = $"add {dest}, {source}",
            Length = length,
        };
    }

    private static Instruction add_imm_acc(System.IO.FileStream fs, byte[] buffer)
    {
        byte opcode = buffer[0];
        byte w = (byte)(opcode & 0x01);

        int index = read_data(fs, w, 1, out ushort data);

        return new Instruction
        {
            Disassembly = $"add {registers[8 * w]}, {data.ToString()}",
            Length = index,
        };
    }

    private static Instruction mov_rm_reg(System.IO.FileStream fs, byte[] buffer)
    {
        int length = get_mod_reg_rm_encodings(fs, buffer, out string dest, out string source);

        return new Instruction
        {
            Disassembly = $"mov {dest}, {source}",
            Length = length,
        };
    }

    private static Instruction mov_imm_rm(System.IO.FileStream fs, byte[] buffer)
    {
        int length = get_imm_rm_encodings(fs, buffer, false, out string dest, out string source);

        return new Instruction
        {
            Disassembly = $"mov {dest}, {source}",
            Length = length,
        };
    }

    private static Instruction mov_imm_reg(System.IO.FileStream fs, byte[] buffer)
    {
        int length = get_imm_reg_encodings(fs, buffer, out string dest, out string source);

        return new Instruction
        {
            Disassembly = $"mov {dest}, {source}",
            Length = length,
        };
    }

    private static Instruction sub_rm_reg(System.IO.FileStream fs, byte[] buffer)
    {
        int length = get_mod_reg_rm_encodings(fs, buffer, out string dest, out string source);

        return new Instruction
        {
            Disassembly = $"sub {dest}, {source}",
            Length = length,
        };
    }

    private static Instruction sub_imm_rm(System.IO.FileStream fs, byte[] buffer)
    {
        int length = get_imm_rm_encodings(fs, buffer, true, out string dest, out string source);

        return new Instruction
        {
            Disassembly = $"sub {dest}, {source}",
            Length = length,
        };
    }

    private static Instruction sub_imm_acc(System.IO.FileStream fs, byte[] buffer)
    {
        byte opcode = buffer[0];
        byte w = (byte)(opcode & 0x01);

        int index = read_data(fs, w, 1, out ushort data);

        return new Instruction
        {
            Disassembly = $"sub {registers[8 * w]}, {data.ToString()}",
            Length = index,
        };
    }
}