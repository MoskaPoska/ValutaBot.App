import os, subprocess, sys
os.environ['TwelveDataApiKey'] = '3e0d610500f0414282d471471f59504e'
os.environ['TWELVE_DATA_API_KEY'] = '3e0d610500f0414282d471471f59504e'
os.environ['TARGET_HORIZON_CANDLES'] = '5'
os.environ['MIN_CONFIDENCE'] = '0.60'
os.chdir(r'C:\Users\bural\source\repos\ValutaBot.App\ml_service')
sys.path.insert(0, r'C:\Users\bural\source\repos\ValutaBot.App\ml_service')
exec(open(r'C:\Users\bural\source\repos\ValutaBot.App\ml_service\main.py').read())
