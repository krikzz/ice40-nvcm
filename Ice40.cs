using System;
using System.Text;

namespace xlinker
{
    class iceWarnings
    {
        string[] warning = new string[256];
        int ctr = 0;

        public void newWarning(string str) {
            if (ctr >= warning.Length) return;
            warning[ctr++] = str;
        }

        public void reset() {
            ctr = 0;
        }

        public void print() {
            try {
                if (Console.CursorLeft != 0 && ctr != 0) Console.WriteLine();
            } catch (Exception) { }
            for (int i = 0; i < ctr; i++) Console.WriteLine(warning[i]);
        }

        public int warningsCount() {
            return ctr;
        }
    }

    public class NvcmSignature
    {
        public int chip_id;
        public int vendor_id;
        public int global_serial;
        public int fcrc;
        public int dcrc;
        public int date;
        public int p_status;
        public int time;
        public int chip_serial;
        public bool is_blank;
        public bool is_secured;

        public void printSignature() {

            int dd = date >> 11;
            int mm = ((date >> 7) & 0b1111);
            int yy = (date & 0b1111111) + 2000;

            int tme = time * 2;
            int h = tme / 60 / 60;
            int m = tme / 60 % 60;
            int s = tme % 60;

            Console.WriteLine("chip id: {0:X2}", chip_id);
            Console.WriteLine("vend id: {0:X2}", vendor_id);
            Console.WriteLine("gserial: {0:X8}", global_serial);
            Console.WriteLine("cserial: {0:X8}", chip_serial);
            Console.WriteLine("f-crc  : {0:X4}", fcrc);
            Console.WriteLine("d-crc  : {0:X4}", dcrc);
            Console.WriteLine("date   : {0:D2}:{1:D2}:{2:D4}", dd, mm, yy);
            Console.WriteLine("time   : {0:D2}:{1:D2}:{2:D2}", h, m, s);
            Console.WriteLine("blank  : " + (is_blank ? "yes" : "no"));
            Console.WriteLine("secured: " + (is_secured ? "yes" : "no"));
        }
    }

    class Ice40
    {

        Linker link;
        byte last_status;
        DateTime prog_date;
        iceWarnings w;


        public Ice40(Linker link) {

            this.link = link;
            last_status = 0xff;
            prog_date = DateTime.Now;
            w = new iceWarnings();
        }


        public NvcmSignature getSignature() {

            link.fpgaReset();
            link.spiSS(0);
            link.spiWrite("7E-AA-99-7E-01-0E");
            link.spiSS(1);
            waitStatus(500, 0);

            link.spiSS(0);
            link.spiWrite("82-00-00-20-00-15-F2-F0-A2-00-00-00");
            link.spiSS(1);
            getStatus(8);

            NvcmSignature signature = readSignature();
            if (signature.chip_id != 8) {

                throw new Exception("Unknown chip id: " + signature.chip_id);
            }

            return signature;
        }



        NvcmSignature readSignature() {

            byte[] resp;
            NvcmSignature signature = new NvcmSignature();

            setChip(1);
            resp = read(0x20, 8);

            signature.is_secured = resp[0] == 0 ? false : true;


            setChip(2);
            resp = read(0x00, 8);

            signature.chip_id = resp[0];
            signature.vendor_id = resp[1];
            signature.global_serial = (resp[2] << 24) | (resp[3] << 16) | (resp[4] << 8) | (resp[5] << 0);
            signature.fcrc = (resp[6] << 8) | (resp[7] << 0);

            setChip(2);
            resp = read(0x08, 16);

            signature.dcrc = (resp[0] << 8) | (resp[1] << 0);
            signature.date = (resp[2] << 8) | (resp[3] << 0);
            signature.p_status = resp[4];
            signature.time = (resp[6] << 8) | (resp[7] << 0);
            signature.is_blank = false;
            if (signature.fcrc == 0 & signature.dcrc == 0) signature.is_blank = true;


            signature.chip_serial = (resp[8] << 24) | (resp[9] << 16) | (resp[10] << 8) | (resp[11] << 0);

            return signature;
        }



        void waitRdy() {

            link.spiWrite("FF-FF");
            link.spiDelayIfBusy();

            for (int i = 0; i < 5; i++) {
                link.spiSS(0);
                link.spiWrite("05-00");
                link.spiSS(1);
                link.spiDelayIfBusy();
                link.spiWrite("FF");
            }
        }




        void setChip(byte chip) {
            if (chip > 2) throw new Exception("unknow chip address: " + chip);
            link.spiSS(0);
            link.spiWrite("83-00-00-25-" + chip + "0");
            link.spiSS(1);

            getStatus(128);
        }

        byte[] read(int addr, int len) {
            byte[] buff = new byte[13];
            buff[0] = 3;
            buff[1] = (byte)(addr >> 16);
            buff[2] = (byte)(addr >> 8);
            buff[3] = (byte)(addr);

            link.spiSS(0);
            link.spiWrite(buff);//send address
            buff = link.spiRead(len);//read data
            link.spiSS(1);

            getStatus(128);

            return buff;
        }


        void waitStatus(int delay_time, byte status) {
            for (int i = 0; i < 16; i++) {
                if (getStatus(delay_time) == status) return;
            }
            throw new Exception("ICE status timeout ");

        }

        byte getStatus(int delay_time) {
            byte resp;

            if (delay_time != 0) {

                byte[] buff = new byte[delay_time];
                for (int i = 0; i < buff.Length; i++) buff[i] = 0xff;
                link.spiWrite(buff);
            }

            link.spiSS(0);
            //resp = link.spiRW("05-00")[1];
            link.spiWrite("05");
            resp = link.spiRead(1)[0];
            link.spiSS(1);
            link.spiWrite("FF");

            last_status = resp;
            return resp;
        }

        void sendStream(string stream) {

            int end = 0;
            string cmd;
            int ctr = 0;
            int len;


            for (int i = 0; i < stream.Length;) {
                i = end;
                end = stream.IndexOf(Environment.NewLine, i) + 1;
                len = end - i;
                if (end == 0) break;
                if (len <= 2) continue;//for double new line
                cmd = stream.Substring(i, len);
                cmd = cmd.TrimEnd();
                cmd = cmd.TrimStart();
                cmd = cmd.Replace(" ", "-");

                if (cmd[0] == '#') continue;


                link.spiSS(0);
                link.spiWrite(cmd);
                link.spiSS(1);


                waitRdy();
                if (ctr++ % 64 == 63) Console.Write(".");
            }


        }

        void cleanCram() {

            byte[] buff = new byte[34125];
            buff[0] = 0x03;

            setChip(0);
            link.spiWrite(buff);
        }


        void writeInfMem() {
            byte[] resp;
            string stream =
                "06\r\n" +
                "02-00-00-20-00-45-F2-F1-C4-00-00-00\r\n" +
                "02-00-00-60-00-45-F2-F1-C4-00-00-00\r\n" +
                "02-00-00-A0-00-45-F2-F1-C4-00-00-00\r\n" +
                "02-00-00-E0-00-45-F2-F1-C4-00-00-00\r\n" +
                "04\r\n";

            setChip(0);
            setChip(1);
            sendStream(stream);

            resp = read(0x20, 8);

            setChip(0);
            setChip(0);

            string str = BitConverter.ToString(resp);
            if (str.EndsWith("00-45-F2-F1-C4-00-00-00") == false) {
                w.newWarning("WARNING: inf memory check failed!");
                w.newWarning(" inf data: " + str);
            }
        }


        string getDate() {

            byte[] buff = new byte[2];

            int time = (prog_date.Day << 11) | (prog_date.Month << 7) | prog_date.Year % 100;
            buff[0] = (byte)(time >> 8);
            buff[1] = (byte)(time);

            return BitConverter.ToString(buff);
        }

        string getTime() {

            byte[] buff = new byte[2];

            int time = (prog_date.Hour * 60 * 60 + prog_date.Minute * 60 + prog_date.Second) / 2;
            buff[0] = (byte)(time >> 8);
            buff[1] = (byte)(time);

            return BitConverter.ToString(buff);
        }


        void writeCfgMem(string nvcm, string serial) {

            byte[] resp;
            string resp1, resp2;
            int idx;

            string stream =
                "06\r\n" +
                "02-00-00-00-00-XX-XX-XX-XX-XX-XX-XX\r\n" +
                "02-00-00-08-YY-YY-YY-YY-YY-YY-YY-YY\r\n" +

                "02-00-00-40-00-XX-XX-XX-XX-XX-XX-XX\r\n" +
                "02-00-00-48-YY-YY-YY-YY-YY-YY-YY-YY\r\n" +

                "02-00-00-80-00-XX-XX-XX-XX-XX-XX-XX\r\n" +
                "02-00-00-88-YY-YY-YY-YY-YY-YY-YY-YY\r\n" +

                "02-00-00-C0-00-XX-XX-XX-XX-XX-XX-XX\r\n" +
                "02-00-00-C8-YY-YY-YY-YY-YY-YY-YY-YY\r\n";


            string verndor = "60";
            string global_serial = serial;// BitConverter.ToString(nvcm_serial);// "03-07-00-00";
            string fcrc = "00-00";

            string dcrc = "00-00";
            string date = getDate();
            string p_status = "01";
            string time = getTime();


            string cfg_0;
            string cfg_1;

            idx = nvcm.IndexOf("#FC ");
            if (idx < 0) {
                w.newWarning("WARNING: FCRC not found");
            } else {
                fcrc = nvcm.Substring(idx + 4, 2) + "-" + nvcm.Substring(idx + 6, 2);
                fcrc = fcrc.ToUpper();
            }

            idx = nvcm.IndexOf("#DC ");
            if (idx < 0) {
                w.newWarning("WARNING: DCRC not found");
            } else {
                dcrc = nvcm.Substring(idx + 4, 2) + "-" + nvcm.Substring(idx + 6, 2);
                dcrc = dcrc.ToUpper();
            }


            cfg_0 = verndor + "-" + global_serial + "-" + fcrc;
            cfg_1 = dcrc + "-" + date + "-" + p_status + "-00-" + time;

            stream = stream.Replace("XX-XX-XX-XX-XX-XX-XX", cfg_0);
            stream = stream.Replace("YY-YY-YY-YY-YY-YY-YY-YY", cfg_1);


            setChip(2);
            sendStream(stream);

            resp = read(0x00, 8);
            resp1 = BitConverter.ToString(resp, 1, 7);

            resp = read(0x08, 8);
            resp2 = BitConverter.ToString(resp, 0, 8);

            setChip(0);
            setChip(0);

            if (stream.Contains(resp1) == false || stream.Contains(resp2) == false) {

                w.newWarning("WARNING: cfg memory check failed!");
                w.newWarning(" inf data: " + resp1 + " | " + resp2);
            }


        }

        byte[] nvcmToBin(string stream) {

            int end = 0;
            string cmd;
            byte[] nvcm_image = new byte[34112];
            byte[] buff;
            int addr;
            int bank;
            int len;

            for (int i = 0; i < stream.Length;) {
                i = end;
                end = stream.IndexOf(Environment.NewLine, i) + 1;
                len = end - i;
                if (end == 0) break;
                if (len <= 2) continue;//for double new line
                cmd = stream.Substring(i, len);
                cmd = cmd.TrimEnd();
                cmd = cmd.TrimStart();
                cmd = cmd.Replace(" ", "");

                if (cmd[0] == '#') continue;
                if (cmd.Length != 24) continue;

                buff = link.stringToByte(cmd);
                if (buff[0] != 02) continue;

                addr = (buff[1] << 16) | (buff[2] << 8) | (buff[3] << 0);
                bank = addr >> 12;
                addr &= 0b111111111111;
                addr += bank * 328;


                if (addr > nvcm_image.Length) continue;

                Array.Copy(buff, 4, nvcm_image, addr, 8);

            }

            return nvcm_image;
        }


        public byte[] nvcmRead() {

            setChip(0);
            byte[] buff = read(0, 34112);
            return buff;
        }


        public void nvcmWrite(byte[] filedata, byte[] serial) {

            string stream = Encoding.ASCII.GetString(filedata);
            NvcmSignature signature;
            string serial_str = "00-00-00-00";
            if (serial != null) serial_str = BitConverter.ToString(serial, 0, 4);

            if (stream.IndexOf("#FC ") < 0 || stream.IndexOf("#DC ") < 0) throw new Exception("Invalid nvcm file");

            w.reset();

            signature = getSignature();

            if (signature.is_secured) {
                throw new Exception("Programming failed. Device is secured!");
            }

            cleanCram();
            
            sendStream(stream);

            if (signature.is_blank) {

                writeInfMem();
                writeCfgMem(stream, serial_str);
            }

            w.print();
            if(w.warningsCount() != 0) {
                Console.WriteLine("");
            }

        }




        public void nvcmVerify(byte[] filedata) {


            string stream = Encoding.ASCII.GetString(filedata);
            byte[] chip_data = nvcmRead();
            byte[] stream_data = nvcmToBin(stream);



            for (int i = 0; i < stream_data.Length; i++) {


                if (chip_data[i] != stream_data[i]) {
                    throw new Exception("verification error at " + i);
                }
            }

        }


        public void nvcmSecure() {

            NvcmSignature signature;

            string stream =
                "06\r\n" +
                "02-00-00-20-30-00-00-01-00-00-00-00\r\n" +
                "02-00-00-60-30-00-00-01-00-00-00-00\r\n" +
                "02-00-00-A0-30-00-00-01-00-00-00-00\r\n" +
                "00-20-00-0E-03-00-00-01-00-00-00-00\r\n" +
                "04\r\n";

            signature = getSignature();
            if (signature.is_secured) {
                throw new Exception("already secured");
            }

            setChip(1);
            sendStream(stream);

            signature = getSignature();

            if (!signature.is_secured) {
                throw new Exception("Secure operation error");
            }

        }

        public void cramWrite(byte[] filedata) {

            link.fpgaReset();
            byte[] dummy = new byte[32];
            for (int i = 0; i < dummy.Length; i++) dummy[i] = 0xff;

            //link.setSpeed(1);

            link.spiSS(0);
            link.spiWrite(filedata);
            link.spiWrite(dummy);
            link.spiSS(1);

            //link.setSpeed(0);


            if (link.fpgaStatus() == 0) {
                throw new Exception("FPGA configuration error");
            }
        }

        

    }
}
