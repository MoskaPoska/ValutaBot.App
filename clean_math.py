import sys

def process_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    methods_to_remove = [
        'public static double[] ComputeSma',
        'public static double ComputeEma(',
        'public static double[] ComputeRsiArray',
        'public static double ComputeRsi(',
        'public static (double macd, double signal) ComputeMacd',
        'public static (double adx, double plusDi, double minusDi) ComputeTrueAdx',
        'public static double ComputeAtr',
        'public static double CalculateVolatilityRatio',
        'public static double ComputeBollingerZscore',
        'public static (bool bullish, bool bearish) DetectRsiDivergence',
        'public static (double score, double confidence, double rsi, double emaS, double volStrength, double atr) ScoreTimeframe'
    ]

    for method in methods_to_remove:
        idx = content.find(method)
        if idx != -1:
            # find start of line
            start_idx = content.rfind('\n', 0, idx)
            if start_idx == -1: start_idx = 0
            
            # find opening brace
            brace_idx = content.find('{', idx)
            if brace_idx == -1: continue
            
            # find matching closing brace
            brace_count = 1
            curr_idx = brace_idx + 1
            while brace_count > 0 and curr_idx < len(content):
                if content[curr_idx] == '{': brace_count += 1
                elif content[curr_idx] == '}': brace_count -= 1
                curr_idx += 1
                
            end_idx = curr_idx
            
            # remove from start_idx to end_idx
            content = content[:start_idx] + content[end_idx:]
            print(f'Removed {method}')
        else:
            print(f'Method not found: {method}')

    # Save it back
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

process_file(r'C:\Users\bural\source\repos\ValutaBot.App\MiniApp\Engines\MathIndicatorsLibrary.cs')
