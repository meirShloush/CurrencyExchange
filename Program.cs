using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace CurrencyExchange
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            ConsolAppTest();
            ;
        }

        private static void ConsolAppTest()
        {
            var currencyExchange = new CurrencyExchange();

            Console.WriteLine("Welcome to Currency Exchange Services \r\nPlease enter the source currency");
            var src = Console.ReadLine();
            Console.WriteLine("Please enter the target currency");
            var trg = Console.ReadLine();
            var amount = 0;
            while (amount != -1)
            {
                Console.WriteLine("Please enter the amount. To exit, press -1");
                int amt;
                while (!int.TryParse(Console.ReadLine(), out amt))
                {
                    Console.WriteLine("Invalid input, please enter only a number. To exit, press -1");
                }

                amount = amt;
                if (amt == -1)
                {
                    continue;
                }
                var ret = currencyExchange.ConvertCurrencyAsync(amount, src, trg).Result;
                if (ret.errors != null && ret.errors.Any())
                {
                    Console.WriteLine(string.Join(", ", ret.errors));
                }
                if (!string.IsNullOrEmpty(ret?.target?.amount.ToString()))
                {
                    Console.WriteLine($"{ret.source.amount} {ret.source.currency} equal to {ret.target.amount} {ret.target.currency}");
                }
            }
        }
    }

    public interface ICache
    {
        void UpdateMemCache(CoinapiResponse response);
        SaveInCache? GetValueFromMemCache(string sourceCurrency, string targetCurrency);
    }

    public sealed class Cache : ICache
    {
        private static int UpdateTheCacheInMinute;
        private Dictionary<string, Dictionary<string, SaveInCache>> MemCache;

        private static readonly Lazy<Cache> lazy = new Lazy<Cache>(() => new Cache());
        public static Cache Instance => lazy.Value;

        private Cache(int updateTheCache = 1)
        {
            if (MemCache == null)
            {
                MemCache = new Dictionary<string, Dictionary<string, SaveInCache>>();
            }
            UpdateTheCacheInMinute = updateTheCache;
        }

        public void UpdateMemCache(CoinapiResponse response)
        {
            try
            {
                var sourceCurrency = response.asset_id_base.ToUpper();
                var targetCurrency = response.asset_id_quote.ToUpper();

                if (!MemCache.ContainsKey(sourceCurrency))
                {
                    MemCache.Add(sourceCurrency, new Dictionary<string, SaveInCache>());
                }

                if (!MemCache[sourceCurrency].ContainsKey(targetCurrency))
                {
                    MemCache[sourceCurrency].Add(targetCurrency, new SaveInCache() { time = response.time, rate = (double)response.rate });
                    return;
                }

                if (!ChackDateValidity(MemCache[sourceCurrency][targetCurrency].time))
                {
                    MemCache[sourceCurrency][targetCurrency] = new SaveInCache() { time = response.time, rate = (double)response.rate };
                }
            }
            catch (Exception ex)
            {
                // Any log
            }
        }

        public SaveInCache? GetValueFromMemCache(string sourceCurrency, string targetCurrency)
        {
            if (MemCache.ContainsKey(sourceCurrency)
                && MemCache[sourceCurrency].ContainsKey(targetCurrency)
                && ChackDateValidity(MemCache[sourceCurrency][targetCurrency].time))
            {
                return MemCache[sourceCurrency][targetCurrency];
            }

            return null;
        }

        private static bool ChackDateValidity(DateTime date)
        {
            if (date.AddMinutes(UpdateTheCacheInMinute) > DateTime.UtcNow)
            {
                return true;
            }
            return false;
        }
    }

    public sealed class CurrencyExchange : ICurrencyExchange
    {
        public async Task<CurrencyExchangeResponse> ConvertCurrencyAsync(double amount, string sourceCurrency, string targetCurrency, bool notTakeFromTheCache = false)
        {
            try
            {
                sourceCurrency = sourceCurrency?.ToUpper();
                targetCurrency = targetCurrency?.ToUpper();

                var errorRequest = CheckValidateRequest(amount, sourceCurrency, targetCurrency);
                if (errorRequest != null)
                {
                    return errorRequest;
                }

                if (!notTakeFromTheCache)
                {
                    var dataFromCache = GetDataFromCache(amount, sourceCurrency, targetCurrency);
                    if (dataFromCache != null)
                    {
                        return dataFromCache;
                    }
                }
                ;

                var ret = await GetDataFromAPIAsync(amount, sourceCurrency, targetCurrency);
                return ret;
            }
            catch (Exception ex)
            {
                return new CurrencyExchangeResponse
                {
                    date = DateTime.UtcNow,
                    errors = new List<string> { $"Internal error: {ex.Message}" }
                };
            }
        }

        private CurrencyExchangeResponse GetDataFromCache(double amount, string sourceCurrency, string targetCurrency)
        {
            var response = Cache.Instance.GetValueFromMemCache(sourceCurrency, targetCurrency);
            if (response == null)
            {
                return null;
            }

            return new CurrencyExchangeResponse
            {
                date = response.Value.time,
                source = new AmountAndCurrency
                {
                    amount = amount,
                    currency = sourceCurrency
                },
                target = new AmountAndCurrency
                {
                    amount = amount * response.Value.rate,
                    currency = targetCurrency
                }
            };
        }

        private async Task<CurrencyExchangeResponse> GetDataFromAPIAsync(double amount, string sourceCurrency, string targetCurrency)
        {
            CoinapiResponse response = null;
            try
            {
                response = await GetExchangeRateFromAPIAsync(sourceCurrency, targetCurrency);
                if (response == null || response.rate == null || response.rate <= 0)
                {
                    return new CurrencyExchangeResponse
                    {
                        date = DateTime.UtcNow,
                        errors = new List<string> { "An error occurred while accessing the API" }
                    };
                }

                Cache.Instance.UpdateMemCache(response);

                return new CurrencyExchangeResponse
                {
                    date = response.time,
                    source = new AmountAndCurrency
                    {
                        amount = amount,
                        currency = sourceCurrency,
                    },
                    target = new AmountAndCurrency
                    {
                        amount = amount * (double)(response.rate),
                        currency = targetCurrency
                    }
                };
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private CurrencyExchangeResponse CheckValidateRequest(double amount, string sourceCurrency, string targetCurrency)
        {
            var errors = new List<string>();
            if (amount <= 0)
            {
                errors.Add("The amount must be a positive number");
            }

            if (string.IsNullOrEmpty(sourceCurrency))
            {
                errors.Add("The source currency cannot be null or empty");
            }
            else if (sourceCurrency.Length != 3)
            {
                errors.Add("The source currency must be 3 characters long");
            }

            if (string.IsNullOrEmpty(targetCurrency))
            {
                errors.Add("The target currency cannot be null or empty");
            }
            else if (targetCurrency.Length != 3)
            {
                errors.Add("The target currency must be 3 characters long");
            }

            if (errors.Any())
            {
                return new CurrencyExchangeResponse
                {
                    date = DateTime.UtcNow,
                    errors = errors
                };
            }

            if (sourceCurrency == targetCurrency)
            {
                return new CurrencyExchangeResponse
                {
                    date = DateTime.UtcNow,
                    source = new AmountAndCurrency
                    {
                        amount = amount,
                        currency = sourceCurrency,
                    },
                    target = new AmountAndCurrency
                    {
                        amount = amount,
                        currency = targetCurrency
                    }
                };
            }

            return null;
        }

        private async Task<CoinapiResponse> GetExchangeRateFromAPIAsync(string sourceCurrency, string targetCurrency)
        {
            try
            {
                var response = await SendRequest(sourceCurrency, targetCurrency);
                if (response == null)
                {
                    return null;
                }

                var jsonResponse = JsonConvert.DeserializeObject<CoinapiResponse>(response);
                return jsonResponse;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static async Task<string> SendRequest(string sourceCurrency, string targetCurrency)
        {
            try
            {
                var url = $"https://rest.coinapi.io/v1/exchangerate/{sourceCurrency}/{targetCurrency}";

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Headers["X-CoinAPI-Key"] = "C804C33F-2E81-416E-9BE4-C963ACC699A6";

                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                //  using (var response = await (HttpWebResponse)request.BeginGetResponse(new AsyncCallback(FinishWebRequest), null))
                using (var response = (HttpWebResponse)await Task.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, null))
                {
                    using (var stream = response.GetResponseStream())
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }

    public interface ICurrencyExchange
    {
        Task<CurrencyExchangeResponse> ConvertCurrencyAsync(double amount, string sourceCurrency, string targetCurrency, bool notTakeFromTheCache = false);
    }

    // DataTypes
    public class CoinapiResponse
    {
        public DateTime time { get; set; }
        public string asset_id_base { get; set; }
        public string asset_id_quote { get; set; }
        public double? rate { get; set; }
    }

    public class CurrencyExchangeResponse
    {
        public List<string> errors { get; set; }
        public DateTime date { get; set; }
        public AmountAndCurrency source { get; set; }
        public AmountAndCurrency target { get; set; }
    }

    public class AmountAndCurrency
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public struct SaveInCache
    {
        public DateTime time { get; set; }
        public double rate { get; set; }
    }
}
