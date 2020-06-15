using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace JsonParser
{
    class Program
    {
        private const bool COPY_NULL_VALUE = false;
        static void Main(string[] args)
        {
            var locate_key = "$id";
            var property_links = new string[] {
                "IsForeignCurrency",
                "ForeignCurrencyId",
                "TotalAmountForeign",
                "AppliedAmountForeign",
                "FreightForeign",
                "ForeignCurrencyCode",
                "PurchaseAmountForeign",
                "DiscountAmountForeign",
                "AmountForeignValue",
                "ImportDutyAmountForeign",
                "TaxAmountForeignValue",
                "Line.Account.ForeignCurrencyId",
            };
            var default_precision_pattern = "F6";
            var fix_precision_patterns = new Dictionary<string, string>() {
                {"Term.DiscountForEarlyPayment", "F1"},
                {"Line.Item.QuantityOnHand", "F1"},
            };

            Func<string, JArray> read_from_file = (path) =>
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var reader = new StreamReader(stream))
                    return JArray.Parse(reader.ReadToEnd());
            };
            Action<string, string> write_to_file = (path, json) =>
            {
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var writer = new StreamWriter(stream))
                    writer.Write(json);
            };
            Func<Queue<string>, JObject, bool, JObject> drilldown_object = null;
            drilldown_object = (links, obj, is_create_point) =>
            {
                Func<Queue<string>, JObject, JObject> step_into_proerties = (links, obj) => {
                    var prop = links.Dequeue();
                    if (obj.Property(prop) is null)
                    {
                        if (is_create_point)
                            obj.Add(prop, new JObject());
                        else
                            return null;
                    }
                    return drilldown_object(links, obj.Property(prop).Value as JObject, is_create_point);
                };
                return !(obj is null) && links.Any() ? step_into_proerties(links, obj) : obj;
            };
            Func<IEnumerable<string>, JObject, bool, (string, JObject)> drilldown_to_end = (links, obj, is_create_point) =>
            {
                var drilldown_properties = new Queue<string>(links.Take(links.Count() - 1));
                return (links.Last(), drilldown_object(drilldown_properties, obj, is_create_point));
            };
            Func<IEnumerable<string>, JObject, JProperty> get_property = (links, obj) =>
            {
                var (last_prop, result) = drilldown_to_end(links, obj, false);
                return result.Property(last_prop);
            };
            Action<IEnumerable<string>, JObject, JValue> set_property = (links, obj, value) =>
            {
                var (last_prop, result) = drilldown_to_end(links, obj, true);
                if (result.Property(last_prop) is null)
                    result.Add(last_prop, value);
                else
                    result.Property(last_prop).Value = value;
            };
            Func<string, JValue, JArray, JObject> get_object = (property_name, value, array) =>
            {
                foreach (JObject obj in array)
                    if (obj.SelectToken(property_name) is JValue val && JValue.Equals(val, value))
                        return obj;
                return null;
            };
            Action<IEnumerable<string>, JObject, JObject> copy_singal = (links, source, target) =>
            {
                if (get_property(links, source)?.Value is JValue val && (COPY_NULL_VALUE || val?.Value != null))
                    set_property(links, target, val);
            };
            Action<IEnumerable<string>, JArray, JArray, string> copy_property = (links, source_array, target_array, locate_key) =>
            {
                foreach (JObject target in target_array)
                {
                    var locate_prop = target.Property(locate_key);
                    var locate_val = locate_prop.Value as JValue;
                    var source = get_object(locate_key, locate_val, source_array);
                    copy_singal(links, source, target);
                }
            };
            Func<IEnumerable<string>, JArray, JArray, string, JArray> copy_properties = (property_links, source_array, target_array, locate_key) =>
            {
                foreach (var property_link in property_links)
                {
                    copy_property(
                        property_link.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries),
                        source_array, target_array, locate_key
                    );
                }
                return target_array;
            };

            write_to_file(
                @"json/3.json",
                JsonConvert.SerializeObject(
                    copy_properties(
                        property_links,
                        read_from_file(@"json/2.json"),
                        read_from_file(@"json/1.json"),
                        locate_key
                    ),
                    Formatting.Indented,
                    new MyDoubleJsonConverter(default_precision_pattern, fix_precision_patterns)
                )
            );
        }
    }
    public class MyDoubleJsonConverter : JsonConverter<Double>
    {
        string DefaultPrecisionPattern;
        Dictionary<string, string> FixPrecisionPatterns;
        public MyDoubleJsonConverter(string defaultPrecisionPattern, Dictionary<string, string> fixPrecisionPatterns)
        {
            this.DefaultPrecisionPattern = defaultPrecisionPattern;
            this.FixPrecisionPatterns = fixPrecisionPatterns;
        }
        public override double ReadJson(JsonReader reader, Type objectType, [AllowNull] double existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
        public override void WriteJson(JsonWriter writer, [AllowNull] double value, JsonSerializer serializer)
        {
            var path = writer.Path.Substring(writer.Path.IndexOf('.') + 1);
            if (this.FixPrecisionPatterns.Keys.Contains(path))
                writer.WriteRawValue(((decimal)value).ToString(this.FixPrecisionPatterns[path], System.Globalization.CultureInfo.InvariantCulture));
            else
                writer.WriteRawValue(((decimal)value).ToString(this.DefaultPrecisionPattern, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}