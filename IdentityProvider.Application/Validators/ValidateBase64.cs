namespace IdentityProvider.Application.Validators
{
    public static class ValidateBase64
    {
        static readonly byte[] _pngHeader = { 0x89, 0x50, 0x4E, 0x47, 0xD, 0xA, 0x1A, 0xA };
        public static bool IsValidPng(this string base64Input)
        {
            if (string.IsNullOrEmpty(base64Input))
            {
                return true;

            }
            try
            {
           
                byte[] bytes = Convert.FromBase64String(base64Input);

                if (!HasPngSignature(bytes))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
        static bool HasPngSignature(byte[] bytes)
        {


            if (bytes.Length < 8) return false;

            for (int i = 0; i < 8; i++)
                if (bytes[i] != _pngHeader[i])
                    return false;

            return true;
        }
    }
}
