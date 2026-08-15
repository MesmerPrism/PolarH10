(function installFormulaPreview(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  if (root) root.PolarFormulaPreview = api;
})(typeof globalThis === "object" ? globalThis : this, () => {
  "use strict";

  const sourceMap = Object.freeze({
    ecg: { variables: ["ecg"], rate: 130, inputLabel: "ECG input", inputUnit: "µV" },
    accelerometer: { variables: ["x", "y", "z"], rate: 200, inputLabel: "ACC magnitude input", inputUnit: "mg" },
    heartRate: { variables: ["hr"], rate: 0, inputLabel: "Heart-rate input", inputUnit: "bpm" },
    rrInterval: { variables: ["rr"], rate: 0, inputLabel: "RR input", inputUnit: "ms" },
  });

  const commonKeys = [
    { label: "+", insert: " + ", title: "Add two values." },
    { label: "−", insert: " - ", title: "Subtract the value on the right." },
    { label: "×", insert: " * ", title: "Multiply two values." },
    { label: "÷", insert: " / ", title: "Divide by the value on the right." },
    { label: "power", insert: " ^ 2", title: "Raise a value to a power; ^ 2 squares it." },
    { label: "( )", insert: "()", cursorBack: 1, title: "Group operations so they are evaluated together." },
    { label: "abs", insert: "abs()", cursorBack: 1, title: "Absolute value: removes the sign." },
    { label: "sqrt", insert: "sqrt()", cursorBack: 1, title: "Square root." },
    { label: "clamp", insert: "clamp(, 0, 1)", cursorBack: 7, title: "Limit a result to a minimum and maximum." },
    { label: "if", insert: "if(, , )", cursorBack: 6, title: "Choose one result when a condition is true and another when false." },
  ];

  const fixedRateKeys = [
    { label: "moving mean", template: (variable) => `moving_mean(${variable}, 1)`, title: "Average the preceding 1 second; edit the duration as needed." },
    { label: "moving SD", template: (variable) => `moving_std(${variable}, 1)`, title: "Standard deviation over the preceding duration in seconds." },
    { label: "RMS", template: (variable) => `moving_rms(${variable}, 1)`, title: "Root-mean-square amplitude over a time window." },
    { label: "z-score", template: (variable) => `zscore(${variable}, 5)`, title: "Express the current sample relative to a rolling mean and standard deviation." },
    { label: "EMA", template: (variable) => `ema(${variable}, 0.5)`, title: "Exponential moving average with a time constant in seconds." },
    { label: "low-pass", template: (variable) => `lowpass(${variable}, 5)`, title: "Keep slower changes below the cutoff frequency in Hz." },
    { label: "high-pass", template: (variable) => `highpass(${variable}, 0.5)`, title: "Reduce slow drift below the cutoff frequency in Hz." },
    { label: "band-pass", template: (variable) => `bandpass(${variable}, 0.5, 20)`, title: "Keep frequencies between the low and high cutoffs in Hz." },
    { label: "derivative", template: (variable) => `derivative(${variable})`, title: "Rate of change per second." },
    { label: "integral", template: (variable) => `integral(${variable})`, title: "Running trapezoidal integral over time." },
  ];

  const rrKeys = [
    { label: "mean NN", insert: "rr_mean(rr, 60)", title: "Mean accepted RR interval over a duration in seconds." },
    { label: "mean HR", insert: "rr_mean_hr(rr, 60)", title: "Convert the rolling mean RR interval to beats per minute." },
    { label: "RMSSD", insert: "rr_rmssd(rr, 60)", title: "Root mean square of successive RR differences over a duration." },
    { label: "lnRMSSD", insert: "rr_ln_rmssd(rr, 60)", title: "Natural logarithm of duration-based RMSSD." },
    { label: "SDNN", insert: "rr_sdnn(rr, 60)", title: "Sample standard deviation of RR intervals in the duration window." },
    { label: "pNN50", insert: "rr_pnn50(rr, 60)", title: "Percent of adjacent RR pairs differing by more than 50 ms." },
    { label: "SD1", insert: "rr_sd1(rr, 60)", title: "Poincaré SD1, equivalent to RMSSD divided by square root of two." },
    { label: "excitement", insert: "excitement(rr, 60)", title: "Causal rolling adaptation of the Excite-O-Meter score." },
    { label: "beat RMSSD", insert: "rmssd(rr, 60)", title: "RMSSD over a fixed count of RR intervals rather than seconds." },
    { label: "beat pNN50", insert: "pnn50(rr, 60)", title: "pNN50 over a fixed count of RR intervals." },
  ];

  function keypad(source) {
    const detail = sourceMap[source] || sourceMap.ecg;
    const variables = detail.variables.map((variable) => ({
      label: variable,
      insert: variable,
      title: variableDescription(variable),
    }));
    const functions = source === "rrInterval"
      ? rrKeys
      : source === "heartRate"
        ? [
          { label: "moving mean", insert: "moving_mean_n(hr, 10)", title: "Average the latest number of heart-rate events." },
          { label: "moving SD", insert: "moving_std_n(hr, 10)", title: "Standard deviation across the latest number of heart-rate events." },
        ]
        : fixedRateKeys.map((entry) => ({ ...entry, insert: entry.template(detail.variables[0]) }));
    return { variables, common: commonKeys, functions };
  }

  function variableDescription(variable) {
    return ({
      ecg: "ECG amplitude in microvolts at the current 130 Hz sample. Time is the chart's automatic x-axis.",
      x: "Accelerometer X-axis value in milli-g at the current sample.",
      y: "Accelerometer Y-axis value in milli-g at the current sample.",
      z: "Accelerometer Z-axis value in milli-g at the current sample.",
      hr: "Device-reported heart rate in beats per minute at the current event.",
      rr: "Current accepted beat-to-beat interval in milliseconds. HRV functions retain its history.",
    })[variable] || variable;
  }

  function preview(fixture, formula) {
    if (!fixture) throw new Error("Recorded Polar H10 preview data is still loading.");
    const source = sourceMap[formula.source];
    if (!source) throw new Error("Choose a supported formula source.");
    const ast = parse(String(formula.expression || ""));
    const frames = sourceFrames(fixture, formula.source);
    const states = new Map();
    const output = [];
    for (const frame of frames) {
      const value = evaluate(ast, frame, source, states);
      if (typeof value === "number" && Number.isFinite(value)) output.push({ time: frame.time, value });
    }
    if (!output.length) throw new Error("The formula has no finite values yet; check its source, window, or denominator.");
    const end = frames.at(-1)?.time || 0;
    const start = Math.max(0, end - 12);
    const input = frames.filter((frame) => frame.time >= start).map((frame) => ({
      time: frame.time,
      value: inputValue(frame, formula.source),
    }));
    const visibleOutput = output.filter((sample) => sample.time >= start);
    return {
      input,
      output: visibleOutput,
      current: visibleOutput.at(-1)?.value,
      inputLabel: source.inputLabel,
      inputUnit: source.inputUnit,
      note: `Recorded Polar H10 data · ${visibleOutput.length.toLocaleString()} finite samples · traces auto-scaled`,
    };
  }

  function sourceFrames(fixture, source) {
    if (source === "ecg") return fixture.ecg.microvolts.map((ecg, index) => ({ ecg, time: index / fixture.ecg.sampleRateHz }));
    if (source === "accelerometer") return fixture.accelerometer.samples.map(([x, y, z], index) => ({ x, y, z, time: index / fixture.accelerometer.sampleRateHz }));
    if (source === "heartRate") return (fixture.metricEvents || []).map((event) => ({ hr: Number(event.heartRateBpm), time: event.offsetMs / 1000 }));
    const rr = [];
    for (const event of fixture.metricEvents || []) {
      const intervals = event.rrIntervalsMs || [];
      let beforeMs = intervals.slice(1).reduce((sum, value) => sum + value, 0);
      for (const interval of intervals) {
        rr.push({ rr: Number(interval), time: Math.max(0, (event.offsetMs - beforeMs) / 1000) });
        beforeMs -= interval;
      }
    }
    return rr.sort((left, right) => left.time - right.time);
  }

  function inputValue(frame, source) {
    if (source === "accelerometer") return Math.hypot(frame.x, frame.y, frame.z);
    return frame.ecg ?? frame.hr ?? frame.rr;
  }

  function tokenize(expression) {
    if (!expression.trim()) throw new Error("Enter an expression or load a metric template.");
    const tokens = [];
    let index = 0;
    while (index < expression.length) {
      const rest = expression.slice(index);
      const whitespace = rest.match(/^\s+/);
      if (whitespace) { index += whitespace[0].length; continue; }
      const number = rest.match(/^(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?/i);
      if (number) { tokens.push({ type: "number", value: Number(number[0]) }); index += number[0].length; continue; }
      const identifier = rest.match(/^[A-Za-z_][A-Za-z0-9_]*/);
      if (identifier) { tokens.push({ type: "identifier", value: identifier[0] }); index += identifier[0].length; continue; }
      const operator = ["==", "!=", ">=", "<=", "&&", "||"].find((candidate) => rest.startsWith(candidate)) || rest[0];
      if (!"()+-*/%^,><!".includes(operator[0]) && !["==", "!=", ">=", "<=", "&&", "||"].includes(operator)) {
        throw new Error(`Unsupported character '${rest[0]}'.`);
      }
      tokens.push({ type: operator === "(" || operator === ")" || operator === "," ? operator : "operator", value: operator });
      index += operator.length;
    }
    tokens.push({ type: "end", value: "" });
    return tokens;
  }

  function parse(expression) {
    const tokens = tokenize(expression);
    let cursor = 0;
    let nodeId = 0;
    const current = () => tokens[cursor];
    const advance = () => tokens[cursor++];
    const precedence = { "||": 1, "&&": 2, "==": 3, "!=": 3, ">": 3, ">=": 3, "<": 3, "<=": 3, "+": 4, "-": 4, "*": 5, "/": 5, "%": 5, "^": 6 };
    const expressionNode = (minimum = 0) => {
      let left = prefix();
      while (current().type === "operator" && (precedence[current().value] || 0) >= minimum) {
        const operator = advance().value;
        const priority = precedence[operator];
        const right = expressionNode(operator === "^" ? priority : priority + 1);
        left = { id: nodeId++, type: "binary", operator, left, right };
      }
      return left;
    };
    const prefix = () => {
      const token = advance();
      if (token.type === "number") return { id: nodeId++, type: "number", value: token.value };
      if (token.type === "identifier") {
        if (current().type !== "(") return { id: nodeId++, type: "variable", name: token.value };
        advance();
        const argumentsList = [];
        if (current().type !== ")") {
          do {
            argumentsList.push(expressionNode());
            if (current().type !== ",") break;
            advance();
          } while (true);
        }
        if (advance().type !== ")") throw new Error(`Expected ')' after ${token.value} arguments.`);
        return { id: nodeId++, type: "call", name: token.value, arguments: argumentsList };
      }
      if (token.type === "(" ) {
        const value = expressionNode();
        if (advance().type !== ")") throw new Error("Expected a closing parenthesis.");
        return value;
      }
      if (token.type === "operator" && ["+", "-", "!"].includes(token.value)) {
        return { id: nodeId++, type: "unary", operator: token.value, value: expressionNode(7) };
      }
      throw new Error("Expected a number, variable, or function.");
    };
    const ast = expressionNode();
    if (current().type !== "end") throw new Error("Unexpected text after the expression.");
    return ast;
  }

  function evaluate(node, frame, source, states) {
    if (node.type === "number") return node.value;
    if (node.type === "variable") {
      if (node.name === "pi") return Math.PI;
      if (node.name === "e") return Math.E;
      if (node.name === "true") return true;
      if (node.name === "false") return false;
      if (!source.variables.includes(node.name)) throw new Error(`'${node.name}' is not available for this source.`);
      return frame[node.name];
    }
    if (node.type === "unary") {
      const value = evaluate(node.value, frame, source, states);
      return node.operator === "-" ? -value : node.operator === "!" ? !value : +value;
    }
    if (node.type === "binary") {
      const left = evaluate(node.left, frame, source, states);
      if (node.operator === "&&" && !left) return false;
      if (node.operator === "||" && left) return true;
      const right = evaluate(node.right, frame, source, states);
      return ({
        "+": () => left + right, "-": () => left - right, "*": () => left * right,
        "/": () => left / right, "%": () => left % right, "^": () => left ** right,
        "==": () => left === right, "!=": () => left !== right, ">": () => left > right,
        ">=": () => left >= right, "<": () => left < right, "<=": () => left <= right,
        "&&": () => left && right, "||": () => left || right,
      })[node.operator]();
    }
    if (node.name === "if") {
      const condition = evaluate(node.arguments[0], frame, source, states);
      return evaluate(node.arguments[condition ? 1 : 2], frame, source, states);
    }
    const values = node.arguments.map((argument) => evaluate(argument, frame, source, states));
    if (isStateful(node.name)) return evaluateStateful(node, values, frame, source, states);
    return evaluatePure(node.name, values);
  }

  function isStateful(name) {
    return /^(?:delay|moving_(?:mean|rms|std)|zscore|ema|lowpass|highpass|bandpass|derivative|integral|moving_(?:mean|rms|std)_n|zscore_n|rmssd|pnn50|rr_(?:mean|mean_hr|rmssd|ln_rmssd|sdnn|pnn50|sd1)|excitement|breathing_(?:magnitude|phase))$/.test(name);
  }

  function evaluatePure(name, values) {
    const functions = {
      abs: Math.abs, sqrt: Math.sqrt, cbrt: Math.cbrt, exp: Math.exp, ln: Math.log,
      log10: Math.log10, sin: Math.sin, cos: Math.cos, tan: Math.tan, asin: Math.asin,
      acos: Math.acos, atan: Math.atan, floor: Math.floor, ceil: Math.ceil, round: Math.round,
      sign: Math.sign, pow: Math.pow, atan2: Math.atan2, min: Math.min, max: Math.max,
      clamp: (value, low, high) => Math.min(high, Math.max(low, value)), normal_cdf: normalCdf,
    };
    if (!functions[name]) throw new Error(`Function '${name}' is not supported.`);
    return functions[name](...values);
  }

  function evaluateStateful(node, values, frame, source, states) {
    let state = states.get(node.id);
    const input = Number(values[0]);
    if (!state) {
      state = createState(node.name, values, source);
      states.set(node.id, state);
    }
    if (state.kind === "window") {
      state.values.push(input);
      if (state.values.length > state.capacity) state.values.shift();
      return windowValue(state.values, state.metric, input);
    }
    if (state.kind === "delay") {
      state.values.push(input);
      return state.values.length > state.capacity ? state.values.shift() : NaN;
    }
    if (state.kind === "ema") {
      state.value = state.value == null ? input : state.value + state.alpha * (input - state.value);
      return state.value;
    }
    if (state.kind === "lowpass") return lowpass(state, input);
    if (state.kind === "highpass") return highpass(state, input);
    if (state.kind === "bandpass") return lowpass(state.low, highpass(state.high, input));
    if (state.kind === "derivative") {
      const value = state.previous == null ? 0 : (input - state.previous) * source.rate;
      state.previous = input;
      return value;
    }
    if (state.kind === "integral") {
      if (state.previous != null) state.total += (state.previous + input) * 0.5 / source.rate;
      state.previous = input;
      return state.total;
    }
    if (state.kind === "rr") {
      if (input >= 250 && input <= 2500) {
        state.values.push(input);
        if (state.count) while (state.values.length > state.count) state.values.shift();
        else {
          state.retained += input;
          while (state.values.length > 2 && state.retained - state.values[0] >= state.windowMs) state.retained -= state.values.shift();
        }
      }
      return rrMetric(state.values, state.metric);
    }
    if (state.kind === "breathing") return breathingValue(state, values);
    return NaN;
  }

  function createState(name, values, source) {
    const capacity = (seconds) => {
      if (!source.rate) throw new Error(`${name} needs a regularly sampled ECG or ACC source.`);
      return Math.max(1, Math.round(Number(seconds) * source.rate));
    };
    if (["moving_mean", "moving_rms", "moving_std", "zscore"].includes(name)) return { kind: "window", metric: name, capacity: capacity(values[1]), values: [] };
    if (["moving_mean_n", "moving_rms_n", "moving_std_n", "zscore_n"].includes(name)) return { kind: "window", metric: name, capacity: Math.max(2, Math.round(values[1])), values: [] };
    if (name === "delay") return { kind: "delay", capacity: capacity(values[1]), values: [] };
    if (name === "ema") return { kind: "ema", alpha: 1 - Math.exp(-1 / capacity(values[1])), value: null };
    if (name === "lowpass") return firstOrder("lowpass", source.rate, values[1]);
    if (name === "highpass") return firstOrder("highpass", source.rate, values[1]);
    if (name === "bandpass") return { kind: "bandpass", high: firstOrder("highpass", source.rate, values[1]), low: firstOrder("lowpass", source.rate, values[2]) };
    if (name === "derivative") return { kind: "derivative", previous: null };
    if (name === "integral") return { kind: "integral", previous: null, total: 0 };
    if (name === "rmssd" || name === "pnn50") return { kind: "rr", metric: name, count: Math.max(2, Math.round(values[1])), values: [] };
    if (name.startsWith("rr_") || name === "excitement") return { kind: "rr", metric: name, windowMs: Math.max(5, Math.min(300, Number(values[1]))) * 1000, retained: 0, values: [] };
    if (name.startsWith("breathing_")) return { kind: "breathing", phase: name.endsWith("phase"), baseline: [null, null, null], smoothing: [], lows: [], highs: [], lowHead: 0, highHead: 0, index: 0, previous: null, settings: values.slice(3) };
    throw new Error(`Function '${name}' is not supported in previews.`);
  }

  function windowValue(values, metric, current) {
    const mean = values.reduce((sum, value) => sum + value, 0) / values.length;
    const variance = values.reduce((sum, value) => sum + (value - mean) ** 2, 0) / values.length;
    if (metric.includes("rms")) return Math.sqrt(values.reduce((sum, value) => sum + value * value, 0) / values.length);
    if (metric.includes("std")) return Math.sqrt(variance);
    if (metric.startsWith("zscore")) return variance > Number.EPSILON ? (current - mean) / Math.sqrt(variance) : 0;
    return mean;
  }

  function rrMetric(values, metric) {
    if (values.length < 2 || (metric === "excitement" && values.length < 10)) return NaN;
    const mean = values.reduce((sum, value) => sum + value, 0) / values.length;
    const differences = values.slice(1).map((value, index) => value - values[index]);
    const rmssd = Math.sqrt(differences.reduce((sum, value) => sum + value * value, 0) / differences.length);
    if (metric === "rmssd" || metric === "rr_rmssd") return rmssd;
    if (metric === "pnn50" || metric === "rr_pnn50") return 100 * differences.filter((value) => Math.abs(value) > 50).length / differences.length;
    if (metric === "rr_mean") return mean;
    if (metric === "rr_mean_hr") return 60000 / mean;
    if (metric === "rr_ln_rmssd") return Math.log(Math.max(Number.MIN_VALUE, rmssd));
    if (metric === "rr_sdnn") return Math.sqrt(values.reduce((sum, value) => sum + (value - mean) ** 2, 0) / (values.length - 1));
    if (metric === "rr_sd1") return rmssd / Math.SQRT2;
    if (metric === "excitement") return excitement(values);
    return NaN;
  }

  function excitement(values) {
    const history = values.slice(1).map((_, index) => {
      const end = index + 2;
      const window = values.slice(Math.max(0, end - 5), end);
      return rrMetric(window, "rmssd");
    });
    const rrZ = zScore(values.at(-1), values);
    const rmssdZ = zScore(history.at(-1), history);
    return Math.min(1, Math.max(0, 1 - (normalCdf(rrZ) + normalCdf(rmssdZ)) / 2));
  }

  function zScore(value, values) {
    const mean = values.reduce((sum, sample) => sum + sample, 0) / values.length;
    const deviation = Math.sqrt(values.reduce((sum, sample) => sum + (sample - mean) ** 2, 0) / Math.max(1, values.length - 1));
    return deviation > Number.EPSILON ? (value - mean) / deviation : NaN;
  }

  function normalCdf(value) {
    const absolute = Math.abs(value);
    const t = 1 / (1 + 0.2316419 * absolute);
    const polynomial = t * (0.31938154 + t * (-0.35656378 + t * (1.7814779 + t * (-1.821255978 + t * 1.330274429))));
    const upper = 1 - Math.exp(-0.5 * absolute * absolute) / Math.sqrt(2 * Math.PI) * polynomial;
    return value >= 0 ? upper : 1 - upper;
  }

  function firstOrder(kind, rate, cutoff) {
    if (!rate || cutoff <= 0 || cutoff >= rate / 2) throw new Error("Filter cutoff must be above zero and below half the source rate.");
    const dt = 1 / rate;
    const rc = 1 / (2 * Math.PI * cutoff);
    return kind === "lowpass"
      ? { kind, alpha: dt / (rc + dt), value: null }
      : { kind, alpha: rc / (rc + dt), previousInput: null, previousOutput: 0 };
  }

  function lowpass(state, input) {
    state.value = state.value == null ? input : state.value + state.alpha * (input - state.value);
    return state.value;
  }

  function highpass(state, input) {
    const output = state.previousInput == null ? 0 : state.alpha * (state.previousOutput + input - state.previousInput);
    state.previousInput = input;
    state.previousOutput = output;
    return output;
  }

  function breathingValue(state, values) {
    const [x, y, z, axisX, axisY, axisZ, smoothingSeconds, option, invert] = values;
    const enabled = [axisX, axisY, axisZ];
    const vector = [x, y, z].map((value) => Number(value) / 1000);
    let projection = 0;
    let count = 0;
    for (let axis = 0; axis < 3; axis += 1) {
      if (!enabled[axis]) continue;
      if (state.baseline[axis] == null) state.baseline[axis] = vector[axis];
      state.baseline[axis] += (vector[axis] - state.baseline[axis]) * 0.001;
      projection += vector[axis] - state.baseline[axis];
      count += 1;
    }
    projection = projection / Math.max(1, count) * (invert ? -1 : 1);
    state.smoothing.push(projection);
    const windowSize = Math.max(1, Math.round(Number(smoothingSeconds) * 200));
    if (state.smoothing.length > windowSize) state.smoothing.shift();
    const smoothed = state.smoothing.reduce((sum, value) => sum + value, 0) / state.smoothing.length;
    while (state.lows.length > state.lowHead && state.lows.at(-1).value >= smoothed) state.lows.pop();
    while (state.highs.length > state.highHead && state.highs.at(-1).value <= smoothed) state.highs.pop();
    state.lows.push({ index: state.index, value: smoothed });
    state.highs.push({ index: state.index, value: smoothed });
    while (state.lows[state.lowHead]?.index <= state.index - 4000) state.lowHead += 1;
    while (state.highs[state.highHead]?.index <= state.index - 4000) state.highHead += 1;
    const low = state.lows[state.lowHead].value;
    const high = state.highs[state.highHead].value;
    const ready = Math.min(state.index + 1, 4000) >= 200 && high - low >= 0.0005;
    const normalized = ready ? Math.min(1, Math.max(0, (smoothed - low) / (high - low))) : 0.5;
    const sensitivity = state.phase ? Number(option) : 0.6;
    const threshold = 0.00015 + (1 - sensitivity) * 0.00235;
    const delta = state.previous == null ? 0 : normalized - state.previous;
    state.previous = normalized;
    state.index += 1;
    if (state.phase) return !ready ? 0 : delta > threshold ? 1 : delta < -threshold ? -1 : 0;
    return option ? normalized : smoothed;
  }

  return Object.freeze({ keypad, parse, preview, sourceMap, variableDescription });
});
