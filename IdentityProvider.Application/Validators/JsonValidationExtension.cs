using System.Text.Json;

namespace IdentityProvider.Application.Validators
{

    public static class JsonValidationExtension
    {

        public static bool IsValidJson(this string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using var document = JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }


        public static bool IsValidJson(dynamic data)
        {


            try
            {
                var json = data.ToString();
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                using var document = JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
