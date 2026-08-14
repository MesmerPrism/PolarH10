//! Sandboxed scalar formulas for Polar H10 signals.
//!
//! The grammar deliberately has no statements, assignment, loops, strings, or
//! user-defined functions. Expressions are parsed and validated once, then
//! evaluated with a bounded operation budget for each source sample.

use std::{
    collections::{HashMap, VecDeque},
    f64::consts::{E, PI},
};

use polar_h10_core::{AccSample, ExperimentalBreathingClassifier, ExperimentalBreathingSettings};
use serde::{Deserialize, Serialize};
use thiserror::Error;
use uuid::Uuid;

pub const MAX_FORMULAS: usize = 32;
pub const MAX_EXPRESSION_BYTES: usize = 2_048;
pub const MAX_AST_NODES: usize = 256;
pub const MAX_AST_DEPTH: usize = 32;
pub const MAX_STATEFUL_CALLS: usize = 16;
pub const MAX_WINDOW_SECONDS: f64 = 60.0;
pub const MAX_COUNT_WINDOW: usize = 4_096;
pub const MAX_TOTAL_STATE_SAMPLES: usize = 1_000_000;
const MAX_OPERATIONS_PER_SAMPLE: usize = 512;
const FAULT_THRESHOLD: u8 = 10;

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum FormulaSource {
    Ecg,
    Accelerometer,
    HeartRate,
    RrInterval,
}

impl FormulaSource {
    pub fn rate_hz(self) -> f64 {
        match self {
            Self::Ecg => 130.0,
            Self::Accelerometer => 200.0,
            Self::HeartRate | Self::RrInterval => 0.0,
        }
    }

    pub fn stream_type(self) -> &'static str {
        match self {
            Self::Ecg => "ProcessedECG",
            Self::Accelerometer => "ProcessedAccelerometer",
            Self::HeartRate => "ProcessedHeartRate",
            Self::RrInterval => "ProcessedRR",
        }
    }

    pub fn allowed_variables(self) -> &'static [&'static str] {
        match self {
            Self::Ecg => &["ecg"],
            Self::Accelerometer => &["x", "y", "z"],
            Self::HeartRate => &["hr"],
            Self::RrInterval => &["rr"],
        }
    }
}

#[derive(Clone, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CustomFormulaConfig {
    pub id: String,
    pub name: String,
    pub source: FormulaSource,
    pub expression: String,
    pub unit: String,
    #[serde(default = "enabled_by_default")]
    pub enabled: bool,
}

fn enabled_by_default() -> bool {
    true
}

impl CustomFormulaConfig {
    pub fn normalized(mut self) -> Result<Self, FormulaError> {
        if Uuid::parse_str(&self.id).is_err() {
            return Err(FormulaError::field(
                "invalid_id",
                &self.id,
                "Formula ID must be a UUID.",
            ));
        }
        self.name = normalize_formula_name(&self.name)
            .map_err(|message| FormulaError::field("invalid_name", &self.id, message))?;
        self.unit = self.unit.trim().to_string();
        if self.unit.is_empty()
            || self.unit.chars().count() > 24
            || self.unit.chars().any(char::is_control)
        {
            return Err(FormulaError::field(
                "invalid_unit",
                &self.id,
                "Unit must contain 1 to 24 printable characters.",
            ));
        }
        self.expression = self.expression.trim().to_string();
        if self.expression.is_empty() || self.expression.len() > MAX_EXPRESSION_BYTES {
            return Err(FormulaError::field(
                "invalid_expression_length",
                &self.id,
                format!("Expression must contain 1 to {MAX_EXPRESSION_BYTES} bytes."),
            ));
        }
        Ok(self)
    }
}

#[derive(Clone, Debug, Error, Serialize)]
#[error("{message}")]
#[serde(rename_all = "camelCase")]
pub struct FormulaError {
    pub code: String,
    pub formula_id: String,
    pub message: String,
    pub start: Option<usize>,
    pub end: Option<usize>,
}

impl FormulaError {
    fn field(code: &str, formula_id: &str, message: impl Into<String>) -> Self {
        Self {
            code: code.into(),
            formula_id: formula_id.into(),
            message: message.into(),
            start: None,
            end: None,
        }
    }

    fn expression(code: &str, formula_id: &str, message: impl Into<String>, span: Span) -> Self {
        Self {
            code: code.into(),
            formula_id: formula_id.into(),
            message: message.into(),
            start: Some(span.start),
            end: Some(span.end),
        }
    }
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FormulaValidation {
    pub normalized: CustomFormulaConfig,
    pub rate_hz: f64,
    pub allowed_variables: &'static [&'static str],
    pub state_samples: usize,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum FormulaRuntimeState {
    Ready,
    WarmingUp,
    Faulted,
}

#[derive(Clone, Debug)]
pub struct FormulaEvaluation {
    pub value: Option<f32>,
    pub state: FormulaRuntimeState,
    pub fault: Option<FormulaError>,
}

#[derive(Clone, Copy, Debug)]
pub struct FormulaFrame {
    pub source: FormulaSource,
    pub ecg: f64,
    pub x: f64,
    pub y: f64,
    pub z: f64,
    pub hr: f64,
    pub rr: f64,
}

impl FormulaFrame {
    pub fn ecg(value: i32) -> Self {
        Self::empty(FormulaSource::Ecg).with_ecg(f64::from(value))
    }

    pub fn accelerometer(sample: AccSample) -> Self {
        let mut frame = Self::empty(FormulaSource::Accelerometer);
        frame.x = f64::from(sample.x_mg);
        frame.y = f64::from(sample.y_mg);
        frame.z = f64::from(sample.z_mg);
        frame
    }

    pub fn heart_rate(value: u16) -> Self {
        let mut frame = Self::empty(FormulaSource::HeartRate);
        frame.hr = f64::from(value);
        frame
    }

    pub fn rr_interval(value: f32) -> Self {
        let mut frame = Self::empty(FormulaSource::RrInterval);
        frame.rr = f64::from(value);
        frame
    }

    fn empty(source: FormulaSource) -> Self {
        Self {
            source,
            ecg: 0.0,
            x: 0.0,
            y: 0.0,
            z: 0.0,
            hr: 0.0,
            rr: 0.0,
        }
    }

    fn with_ecg(mut self, value: f64) -> Self {
        self.ecg = value;
        self
    }
}

pub fn validate_formula(config: CustomFormulaConfig) -> Result<FormulaValidation, FormulaError> {
    let compiled = CompiledFormula::compile(config)?;
    Ok(FormulaValidation {
        normalized: compiled.config,
        rate_hz: compiled.source.rate_hz(),
        allowed_variables: compiled.source.allowed_variables(),
        state_samples: compiled.state_samples,
    })
}

pub fn normalize_formula_name(value: &str) -> Result<String, &'static str> {
    let mut normalized = String::with_capacity(value.len());
    let mut separator_pending = false;
    for character in value.trim().chars() {
        if character.is_ascii_alphanumeric() {
            if separator_pending && !normalized.is_empty() && !normalized.ends_with(['_', '-']) {
                normalized.push('_');
            }
            normalized.push(character);
            separator_pending = false;
        } else if character == '-' || character == '_' {
            normalized.push(character);
            separator_pending = false;
        } else {
            separator_pending = true;
        }
    }
    let normalized = normalized.trim_matches(['_', '-']).to_string();
    if normalized.is_empty() {
        return Err("Formula name must contain at least one letter or number.");
    }
    if normalized.chars().count() > 48 {
        return Err("Formula name must be 48 characters or fewer.");
    }
    Ok(normalized)
}

pub struct CompiledFormula {
    config: CustomFormulaConfig,
    source: FormulaSource,
    ast: Node,
    states: HashMap<usize, DspState>,
    state_samples: usize,
    consecutive_errors: u8,
    faulted: bool,
}

impl CompiledFormula {
    pub fn compile(config: CustomFormulaConfig) -> Result<Self, FormulaError> {
        let config = config.normalized()?;
        let mut parser = Parser::new(&config.expression, &config.id)?;
        let ast = parser.parse()?;
        let mut validation = ValidationState::default();
        validate_node(&ast, &config, 0, &mut validation)?;
        if infer_type(&ast, &config)? != ValueType::Number {
            return Err(FormulaError::expression(
                "non_numeric_result",
                &config.id,
                "Formula result must be numeric.",
                ast.span,
            ));
        }
        if validation.stateful_calls > MAX_STATEFUL_CALLS {
            return Err(FormulaError::field(
                "too_many_stateful_calls",
                &config.id,
                format!("A formula may use at most {MAX_STATEFUL_CALLS} stateful DSP calls."),
            ));
        }
        if validation.state_samples > MAX_TOTAL_STATE_SAMPLES {
            return Err(FormulaError::field(
                "state_budget_exceeded",
                &config.id,
                "Formula DSP windows exceed the allowed state budget.",
            ));
        }
        let mut states = HashMap::new();
        initialize_states(&ast, &config, &mut states)?;
        Ok(Self {
            source: config.source,
            config,
            ast,
            states,
            state_samples: validation.state_samples,
            consecutive_errors: 0,
            faulted: false,
        })
    }

    pub fn config(&self) -> &CustomFormulaConfig {
        &self.config
    }

    pub fn state_samples(&self) -> usize {
        self.state_samples
    }

    pub fn reset(&mut self) -> Result<(), FormulaError> {
        let replacement = Self::compile(self.config.clone())?;
        *self = replacement;
        Ok(())
    }

    pub fn process(&mut self, frame: FormulaFrame) -> FormulaEvaluation {
        if self.faulted {
            return FormulaEvaluation {
                value: None,
                state: FormulaRuntimeState::Faulted,
                fault: None,
            };
        }
        if frame.source != self.source {
            return self.record_error("Formula received a sample from the wrong source.", None);
        }
        let mut budget = MAX_OPERATIONS_PER_SAMPLE;
        match evaluate_node(&self.ast, frame, &mut self.states, &mut budget) {
            Ok(EvalValue::Number(value)) if value.is_finite() => {
                let value = value as f32;
                if value.is_finite() {
                    self.consecutive_errors = 0;
                    FormulaEvaluation {
                        value: Some(value),
                        state: FormulaRuntimeState::Ready,
                        fault: None,
                    }
                } else {
                    self.record_error(
                        "Formula result is outside the float32 stream range.",
                        Some(self.ast.span),
                    )
                }
            }
            Ok(EvalValue::NotReady) => {
                self.consecutive_errors = 0;
                FormulaEvaluation {
                    value: None,
                    state: FormulaRuntimeState::WarmingUp,
                    fault: None,
                }
            }
            Ok(EvalValue::Boolean(_)) => self.record_error(
                "Formula result must be numeric, not boolean.",
                Some(self.ast.span),
            ),
            Ok(EvalValue::Number(_)) => {
                self.record_error("Formula returned NaN or infinity.", Some(self.ast.span))
            }
            Err(error) => self.record_error(&error, Some(self.ast.span)),
        }
    }

    fn record_error(&mut self, message: &str, span: Option<Span>) -> FormulaEvaluation {
        self.consecutive_errors = self.consecutive_errors.saturating_add(1);
        let transitioned = self.consecutive_errors >= FAULT_THRESHOLD;
        if transitioned {
            self.faulted = true;
        }
        FormulaEvaluation {
            value: None,
            state: if transitioned {
                FormulaRuntimeState::Faulted
            } else {
                FormulaRuntimeState::Ready
            },
            fault: transitioned.then(|| {
                span.map_or_else(
                    || FormulaError::field("runtime_fault", &self.config.id, message),
                    |span| {
                        FormulaError::expression("runtime_fault", &self.config.id, message, span)
                    },
                )
            }),
        }
    }
}

#[derive(Clone, Copy, Debug, Default)]
struct Span {
    start: usize,
    end: usize,
}

#[derive(Clone, Debug)]
struct Node {
    id: usize,
    span: Span,
    kind: NodeKind,
}

#[derive(Clone, Debug)]
enum NodeKind {
    Number(f64),
    Boolean(bool),
    Variable(String),
    Unary(UnaryOp, Box<Node>),
    Binary(BinaryOp, Box<Node>, Box<Node>),
    Call(String, Vec<Node>),
}

#[derive(Clone, Copy, Debug)]
enum UnaryOp {
    Positive,
    Negative,
    Not,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum BinaryOp {
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Power,
    Equal,
    NotEqual,
    Greater,
    GreaterEqual,
    Less,
    LessEqual,
    And,
    Or,
}

#[derive(Clone, Debug)]
enum TokenKind {
    Number(f64),
    Identifier(String),
    LeftParen,
    RightParen,
    Comma,
    Operator(&'static str),
    End,
}

#[derive(Clone, Debug)]
struct Token {
    kind: TokenKind,
    span: Span,
}

struct Parser<'a> {
    formula_id: &'a str,
    tokens: Vec<Token>,
    cursor: usize,
    next_node_id: usize,
}

impl<'a> Parser<'a> {
    fn new(expression: &str, formula_id: &'a str) -> Result<Self, FormulaError> {
        Ok(Self {
            formula_id,
            tokens: tokenize(expression, formula_id)?,
            cursor: 0,
            next_node_id: 0,
        })
    }

    fn parse(&mut self) -> Result<Node, FormulaError> {
        let node = self.parse_expression(0)?;
        if !matches!(self.current().kind, TokenKind::End) {
            return Err(self.error("unexpected_token", "Unexpected token after expression."));
        }
        if self.next_node_id > MAX_AST_NODES {
            return Err(FormulaError::field(
                "expression_too_complex",
                self.formula_id,
                format!("Expression may contain at most {MAX_AST_NODES} nodes."),
            ));
        }
        Ok(node)
    }

    fn parse_expression(&mut self, minimum_precedence: u8) -> Result<Node, FormulaError> {
        let mut left = self.parse_prefix()?;
        while let Some((operator, precedence, right_associative)) =
            binary_operator(&self.current().kind)
        {
            if precedence < minimum_precedence {
                break;
            }
            self.advance();
            let right = self.parse_expression(if right_associative {
                precedence
            } else {
                precedence + 1
            })?;
            let span = Span {
                start: left.span.start,
                end: right.span.end,
            };
            left = self.node(
                span,
                NodeKind::Binary(operator, Box::new(left), Box::new(right)),
            );
        }
        Ok(left)
    }

    fn parse_prefix(&mut self) -> Result<Node, FormulaError> {
        let token = self.current().clone();
        match token.kind {
            TokenKind::Number(value) => {
                self.advance();
                Ok(self.node(token.span, NodeKind::Number(value)))
            }
            TokenKind::Identifier(identifier) => {
                self.advance();
                if matches!(self.current().kind, TokenKind::LeftParen) {
                    self.parse_call(identifier, token.span.start)
                } else if identifier == "true" || identifier == "false" {
                    Ok(self.node(token.span, NodeKind::Boolean(identifier == "true")))
                } else {
                    Ok(self.node(token.span, NodeKind::Variable(identifier)))
                }
            }
            TokenKind::Operator("+") | TokenKind::Operator("-") | TokenKind::Operator("!") => {
                self.advance();
                let operation = match token.kind {
                    TokenKind::Operator("+") => UnaryOp::Positive,
                    TokenKind::Operator("-") => UnaryOp::Negative,
                    _ => UnaryOp::Not,
                };
                let value = self.parse_expression(7)?;
                let span = Span {
                    start: token.span.start,
                    end: value.span.end,
                };
                Ok(self.node(span, NodeKind::Unary(operation, Box::new(value))))
            }
            TokenKind::LeftParen => {
                self.advance();
                let mut value = self.parse_expression(0)?;
                let end = self.expect_right_paren()?;
                value.span = Span {
                    start: token.span.start,
                    end,
                };
                Ok(value)
            }
            _ => Err(self.error(
                "expected_value",
                "Expected a number, variable, or function call.",
            )),
        }
    }

    fn parse_call(&mut self, name: String, start: usize) -> Result<Node, FormulaError> {
        self.advance();
        let mut arguments = Vec::new();
        if !matches!(self.current().kind, TokenKind::RightParen) {
            loop {
                arguments.push(self.parse_expression(0)?);
                if matches!(self.current().kind, TokenKind::Comma) {
                    self.advance();
                } else {
                    break;
                }
            }
        }
        let end = self.expect_right_paren()?;
        Ok(self.node(Span { start, end }, NodeKind::Call(name, arguments)))
    }

    fn expect_right_paren(&mut self) -> Result<usize, FormulaError> {
        let token = self.current().clone();
        if !matches!(token.kind, TokenKind::RightParen) {
            return Err(self.error("missing_parenthesis", "Expected ')'."));
        }
        self.advance();
        Ok(token.span.end)
    }

    fn node(&mut self, span: Span, kind: NodeKind) -> Node {
        let id = self.next_node_id;
        self.next_node_id += 1;
        Node { id, span, kind }
    }

    fn current(&self) -> &Token {
        &self.tokens[self.cursor]
    }

    fn advance(&mut self) {
        self.cursor = (self.cursor + 1).min(self.tokens.len() - 1);
    }

    fn error(&self, code: &str, message: &str) -> FormulaError {
        FormulaError::expression(code, self.formula_id, message, self.current().span)
    }
}

fn tokenize(expression: &str, formula_id: &str) -> Result<Vec<Token>, FormulaError> {
    if let Some((start, character)) = expression
        .char_indices()
        .find(|(_, character)| !character.is_ascii())
    {
        return Err(FormulaError::expression(
            "invalid_character",
            formula_id,
            format!("Character '{character}' is not allowed in formulas."),
            Span {
                start,
                end: start + character.len_utf8(),
            },
        ));
    }
    let bytes = expression.as_bytes();
    let mut tokens = Vec::new();
    let mut cursor = 0;
    while cursor < bytes.len() {
        let character = bytes[cursor] as char;
        if character.is_ascii_whitespace() {
            cursor += 1;
            continue;
        }
        let start = cursor;
        if character.is_ascii_digit() || character == '.' {
            cursor += 1;
            while cursor < bytes.len()
                && ((bytes[cursor] as char).is_ascii_digit() || bytes[cursor] == b'.')
            {
                cursor += 1;
            }
            if cursor < bytes.len() && matches!(bytes[cursor], b'e' | b'E') {
                cursor += 1;
                if cursor < bytes.len() && matches!(bytes[cursor], b'+' | b'-') {
                    cursor += 1;
                }
                while cursor < bytes.len() && (bytes[cursor] as char).is_ascii_digit() {
                    cursor += 1;
                }
            }
            let value = expression[start..cursor].parse::<f64>().map_err(|_| {
                FormulaError::expression(
                    "invalid_number",
                    formula_id,
                    "Invalid numeric literal.",
                    Span { start, end: cursor },
                )
            })?;
            tokens.push(Token {
                kind: TokenKind::Number(value),
                span: Span { start, end: cursor },
            });
            continue;
        }
        if character.is_ascii_alphabetic() || character == '_' {
            cursor += 1;
            while cursor < bytes.len()
                && ((bytes[cursor] as char).is_ascii_alphanumeric() || bytes[cursor] == b'_')
            {
                cursor += 1;
            }
            tokens.push(Token {
                kind: TokenKind::Identifier(expression[start..cursor].to_string()),
                span: Span { start, end: cursor },
            });
            continue;
        }
        let (kind, width) = if cursor + 1 < bytes.len() {
            match &expression[cursor..cursor + 2] {
                "==" => (TokenKind::Operator("=="), 2),
                "!=" => (TokenKind::Operator("!="), 2),
                ">=" => (TokenKind::Operator(">="), 2),
                "<=" => (TokenKind::Operator("<="), 2),
                "&&" => (TokenKind::Operator("&&"), 2),
                "||" => (TokenKind::Operator("||"), 2),
                _ => single_character_token(character).ok_or_else(|| {
                    FormulaError::expression(
                        "invalid_character",
                        formula_id,
                        format!("Character '{character}' is not allowed."),
                        Span {
                            start,
                            end: start + 1,
                        },
                    )
                })?,
            }
        } else {
            single_character_token(character).ok_or_else(|| {
                FormulaError::expression(
                    "invalid_character",
                    formula_id,
                    format!("Character '{character}' is not allowed."),
                    Span {
                        start,
                        end: start + 1,
                    },
                )
            })?
        };
        cursor += width;
        tokens.push(Token {
            kind,
            span: Span { start, end: cursor },
        });
    }
    tokens.push(Token {
        kind: TokenKind::End,
        span: Span {
            start: expression.len(),
            end: expression.len(),
        },
    });
    Ok(tokens)
}

fn single_character_token(character: char) -> Option<(TokenKind, usize)> {
    Some((
        match character {
            '(' => TokenKind::LeftParen,
            ')' => TokenKind::RightParen,
            ',' => TokenKind::Comma,
            '+' => TokenKind::Operator("+"),
            '-' => TokenKind::Operator("-"),
            '*' => TokenKind::Operator("*"),
            '/' => TokenKind::Operator("/"),
            '%' => TokenKind::Operator("%"),
            '^' => TokenKind::Operator("^"),
            '>' => TokenKind::Operator(">"),
            '<' => TokenKind::Operator("<"),
            '!' => TokenKind::Operator("!"),
            _ => return None,
        },
        1,
    ))
}

fn binary_operator(kind: &TokenKind) -> Option<(BinaryOp, u8, bool)> {
    let TokenKind::Operator(operator) = kind else {
        return None;
    };
    Some(match *operator {
        "||" => (BinaryOp::Or, 1, false),
        "&&" => (BinaryOp::And, 2, false),
        "==" => (BinaryOp::Equal, 3, false),
        "!=" => (BinaryOp::NotEqual, 3, false),
        ">" => (BinaryOp::Greater, 3, false),
        ">=" => (BinaryOp::GreaterEqual, 3, false),
        "<" => (BinaryOp::Less, 3, false),
        "<=" => (BinaryOp::LessEqual, 3, false),
        "+" => (BinaryOp::Add, 4, false),
        "-" => (BinaryOp::Subtract, 4, false),
        "*" => (BinaryOp::Multiply, 5, false),
        "/" => (BinaryOp::Divide, 5, false),
        "%" => (BinaryOp::Modulo, 5, false),
        "^" => (BinaryOp::Power, 6, true),
        _ => return None,
    })
}

#[derive(Default)]
struct ValidationState {
    nodes: usize,
    stateful_calls: usize,
    state_samples: usize,
}

fn validate_node(
    node: &Node,
    config: &CustomFormulaConfig,
    depth: usize,
    state: &mut ValidationState,
) -> Result<(), FormulaError> {
    state.nodes += 1;
    if state.nodes > MAX_AST_NODES || depth > MAX_AST_DEPTH {
        return Err(FormulaError::expression(
            "expression_too_complex",
            &config.id,
            "Expression is too complex or deeply nested.",
            node.span,
        ));
    }
    match &node.kind {
        NodeKind::Variable(name) => {
            if !matches!(name.as_str(), "pi" | "e")
                && !config.source.allowed_variables().contains(&name.as_str())
            {
                return Err(FormulaError::expression(
                    "unknown_variable",
                    &config.id,
                    format!(
                        "Variable '{name}' is not available for {:?} formulas.",
                        config.source
                    ),
                    node.span,
                ));
            }
        }
        NodeKind::Unary(_, value) => validate_node(value, config, depth + 1, state)?,
        NodeKind::Binary(_, left, right) => {
            validate_node(left, config, depth + 1, state)?;
            validate_node(right, config, depth + 1, state)?;
        }
        NodeKind::Call(name, arguments) => {
            let specification = function_spec(name).ok_or_else(|| {
                FormulaError::expression(
                    "unknown_function",
                    &config.id,
                    format!("Function '{name}' is not supported."),
                    node.span,
                )
            })?;
            if !specification.accepts(arguments.len()) {
                return Err(FormulaError::expression(
                    "wrong_argument_count",
                    &config.id,
                    format!(
                        "Function '{name}' expects {} argument(s).",
                        specification.arity_description()
                    ),
                    node.span,
                ));
            }
            if specification.acc_only && config.source != FormulaSource::Accelerometer {
                return Err(FormulaError::expression(
                    "wrong_source",
                    &config.id,
                    format!("Function '{name}' is available only for accelerometer formulas."),
                    node.span,
                ));
            }
            if specification.fixed_rate_only && config.source.rate_hz() <= 0.0 {
                return Err(FormulaError::expression(
                    "fixed_rate_required",
                    &config.id,
                    format!("Function '{name}' requires an ECG or ACC source clock."),
                    node.span,
                ));
            }
            if specification.stateful {
                state.stateful_calls += 1;
                state.state_samples += state_capacity(name, arguments, config)?;
            }
            for argument in arguments {
                validate_node(argument, config, depth + 1, state)?;
            }
        }
        NodeKind::Number(_) | NodeKind::Boolean(_) => {}
    }
    Ok(())
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum ValueType {
    Number,
    Boolean,
}

fn infer_type(node: &Node, config: &CustomFormulaConfig) -> Result<ValueType, FormulaError> {
    let mismatch = |span: Span, expected: &str| {
        FormulaError::expression("type_mismatch", &config.id, format!("{expected}."), span)
    };
    match &node.kind {
        NodeKind::Number(_) | NodeKind::Variable(_) => Ok(ValueType::Number),
        NodeKind::Boolean(_) => Ok(ValueType::Boolean),
        NodeKind::Unary(operator, value) => {
            let value_type = infer_type(value, config)?;
            let expected = if matches!(operator, UnaryOp::Not) {
                ValueType::Boolean
            } else {
                ValueType::Number
            };
            if value_type != expected {
                return Err(mismatch(
                    value.span,
                    if expected == ValueType::Boolean {
                        "Logical negation requires a Boolean value"
                    } else {
                        "Unary arithmetic requires a numeric value"
                    },
                ));
            }
            Ok(expected)
        }
        NodeKind::Binary(operator, left, right) => {
            let left_type = infer_type(left, config)?;
            let right_type = infer_type(right, config)?;
            match operator {
                BinaryOp::And | BinaryOp::Or => {
                    if left_type != ValueType::Boolean || right_type != ValueType::Boolean {
                        return Err(mismatch(
                            node.span,
                            "Logical operators require Boolean operands",
                        ));
                    }
                    Ok(ValueType::Boolean)
                }
                BinaryOp::Equal | BinaryOp::NotEqual => {
                    if left_type != right_type {
                        return Err(mismatch(
                            node.span,
                            "Equality operands must have the same type",
                        ));
                    }
                    Ok(ValueType::Boolean)
                }
                BinaryOp::Greater
                | BinaryOp::GreaterEqual
                | BinaryOp::Less
                | BinaryOp::LessEqual => {
                    if left_type != ValueType::Number || right_type != ValueType::Number {
                        return Err(mismatch(node.span, "Comparisons require numeric operands"));
                    }
                    Ok(ValueType::Boolean)
                }
                BinaryOp::Add
                | BinaryOp::Subtract
                | BinaryOp::Multiply
                | BinaryOp::Divide
                | BinaryOp::Modulo
                | BinaryOp::Power => {
                    if left_type != ValueType::Number || right_type != ValueType::Number {
                        return Err(mismatch(
                            node.span,
                            "Arithmetic operators require numeric operands",
                        ));
                    }
                    Ok(ValueType::Number)
                }
            }
        }
        NodeKind::Call(name, arguments) => {
            let argument_types: Vec<_> = arguments
                .iter()
                .map(|argument| infer_type(argument, config))
                .collect::<Result<_, _>>()?;
            if name == "if" {
                if argument_types[0] != ValueType::Boolean {
                    return Err(mismatch(
                        arguments[0].span,
                        "The first if argument must be Boolean",
                    ));
                }
                if argument_types[1] != argument_types[2] {
                    return Err(mismatch(
                        node.span,
                        "Both if result branches must have the same type",
                    ));
                }
                return Ok(argument_types[1]);
            }

            let boolean_arguments: &[usize] = match name.as_str() {
                "breathing_magnitude" => &[3, 4, 5, 7, 8],
                "breathing_phase" => &[3, 4, 5, 8],
                _ => &[],
            };
            for (index, argument_type) in argument_types.iter().copied().enumerate() {
                let expected = if boolean_arguments.contains(&index) {
                    ValueType::Boolean
                } else {
                    ValueType::Number
                };
                if argument_type != expected {
                    return Err(mismatch(
                        arguments[index].span,
                        if expected == ValueType::Boolean {
                            "This function argument must be Boolean"
                        } else {
                            "Function arguments must be numeric"
                        },
                    ));
                }
            }
            Ok(ValueType::Number)
        }
    }
}

struct FunctionSpec {
    min_arity: usize,
    max_arity: usize,
    stateful: bool,
    fixed_rate_only: bool,
    acc_only: bool,
}

impl FunctionSpec {
    fn accepts(&self, count: usize) -> bool {
        (self.min_arity..=self.max_arity).contains(&count)
    }

    fn arity_description(&self) -> String {
        if self.min_arity == self.max_arity {
            self.min_arity.to_string()
        } else {
            format!("{} to {}", self.min_arity, self.max_arity)
        }
    }
}

fn function_spec(name: &str) -> Option<FunctionSpec> {
    let pure = |arity| FunctionSpec {
        min_arity: arity,
        max_arity: arity,
        stateful: false,
        fixed_rate_only: false,
        acc_only: false,
    };
    Some(match name {
        "abs" | "sqrt" | "cbrt" | "exp" | "ln" | "log10" | "sin" | "cos" | "tan" | "asin"
        | "acos" | "atan" | "floor" | "ceil" | "round" | "sign" => pure(1),
        "pow" | "atan2" | "min" | "max" => pure(2),
        "clamp" | "if" => pure(3),
        "delay" | "moving_mean" | "moving_rms" | "moving_std" | "zscore" | "ema" | "lowpass"
        | "highpass" => FunctionSpec {
            min_arity: 2,
            max_arity: 2,
            stateful: true,
            fixed_rate_only: true,
            acc_only: false,
        },
        "bandpass" => FunctionSpec {
            min_arity: 3,
            max_arity: 3,
            stateful: true,
            fixed_rate_only: true,
            acc_only: false,
        },
        "derivative" | "integral" => FunctionSpec {
            min_arity: 1,
            max_arity: 1,
            stateful: true,
            fixed_rate_only: true,
            acc_only: false,
        },
        "moving_mean_n" | "moving_rms_n" | "moving_std_n" | "zscore_n" | "rmssd" => FunctionSpec {
            min_arity: 2,
            max_arity: 2,
            stateful: true,
            fixed_rate_only: false,
            acc_only: false,
        },
        "breathing_magnitude" => FunctionSpec {
            min_arity: 9,
            max_arity: 9,
            stateful: true,
            fixed_rate_only: true,
            acc_only: true,
        },
        "breathing_phase" => FunctionSpec {
            min_arity: 9,
            max_arity: 9,
            stateful: true,
            fixed_rate_only: true,
            acc_only: true,
        },
        _ => return None,
    })
}

fn constant_number(node: &Node, config: &CustomFormulaConfig) -> Result<f64, FormulaError> {
    match node.kind {
        NodeKind::Number(value) => Ok(value),
        NodeKind::Unary(UnaryOp::Negative, ref child) => match child.kind {
            NodeKind::Number(value) => Ok(-value),
            _ => Err(constant_argument_error(node, config)),
        },
        _ => Err(constant_argument_error(node, config)),
    }
}

fn constant_boolean(node: &Node, config: &CustomFormulaConfig) -> Result<bool, FormulaError> {
    match node.kind {
        NodeKind::Boolean(value) => Ok(value),
        _ => Err(FormulaError::expression(
            "constant_argument_required",
            &config.id,
            "DSP configuration arguments must be literal true/false values.",
            node.span,
        )),
    }
}

fn constant_argument_error(node: &Node, config: &CustomFormulaConfig) -> FormulaError {
    FormulaError::expression(
        "constant_argument_required",
        &config.id,
        "DSP window and cutoff arguments must be numeric literals.",
        node.span,
    )
}

fn duration_capacity(node: &Node, config: &CustomFormulaConfig) -> Result<usize, FormulaError> {
    let seconds = constant_number(node, config)?;
    let minimum = 1.0 / config.source.rate_hz();
    if !seconds.is_finite() || !(minimum..=MAX_WINDOW_SECONDS).contains(&seconds) {
        return Err(FormulaError::expression(
            "invalid_window",
            &config.id,
            format!("DSP duration must be between {minimum:.4} and {MAX_WINDOW_SECONDS} seconds."),
            node.span,
        ));
    }
    Ok((seconds * config.source.rate_hz()).round().max(1.0) as usize)
}

fn count_capacity(node: &Node, config: &CustomFormulaConfig) -> Result<usize, FormulaError> {
    let count = constant_number(node, config)?;
    if !count.is_finite()
        || count.fract() != 0.0
        || !(2.0..=MAX_COUNT_WINDOW as f64).contains(&count)
    {
        return Err(FormulaError::expression(
            "invalid_window",
            &config.id,
            format!("Count window must be an integer from 2 to {MAX_COUNT_WINDOW}."),
            node.span,
        ));
    }
    Ok(count as usize)
}

fn validate_cutoff(node: &Node, config: &CustomFormulaConfig) -> Result<f64, FormulaError> {
    let cutoff = constant_number(node, config)?;
    if !cutoff.is_finite() || cutoff <= 0.0 || cutoff >= config.source.rate_hz() / 2.0 {
        return Err(FormulaError::expression(
            "invalid_cutoff",
            &config.id,
            "Filter cutoff must be greater than zero and below the source Nyquist frequency.",
            node.span,
        ));
    }
    Ok(cutoff)
}

fn state_capacity(
    name: &str,
    arguments: &[Node],
    config: &CustomFormulaConfig,
) -> Result<usize, FormulaError> {
    match name {
        "delay" | "moving_mean" | "moving_rms" | "moving_std" | "zscore" => {
            duration_capacity(&arguments[1], config)
        }
        "moving_mean_n" | "moving_rms_n" | "moving_std_n" | "zscore_n" | "rmssd" => {
            count_capacity(&arguments[1], config)
        }
        "ema" => {
            let _ = duration_capacity(&arguments[1], config)?;
            Ok(1)
        }
        "lowpass" | "highpass" => {
            validate_cutoff(&arguments[1], config)?;
            Ok(2)
        }
        "bandpass" => {
            let low = validate_cutoff(&arguments[1], config)?;
            let high = validate_cutoff(&arguments[2], config)?;
            if low >= high {
                return Err(FormulaError::expression(
                    "invalid_cutoff",
                    &config.id,
                    "Band-pass low cutoff must be below its high cutoff.",
                    arguments[1].span,
                ));
            }
            Ok(4)
        }
        "derivative" | "integral" => Ok(2),
        "breathing_magnitude" | "breathing_phase" => {
            validate_breathing_arguments(arguments, config)?;
            Ok(8_000)
        }
        _ => Ok(0),
    }
}

fn validate_breathing_arguments(
    arguments: &[Node],
    config: &CustomFormulaConfig,
) -> Result<ExperimentalBreathingSettings, FormulaError> {
    let axes = [
        constant_boolean(&arguments[3], config)?,
        constant_boolean(&arguments[4], config)?,
        constant_boolean(&arguments[5], config)?,
    ];
    if axes.into_iter().filter(|enabled| *enabled).count() < 2 {
        return Err(FormulaError::expression(
            "invalid_breathing_axes",
            &config.id,
            "Breathing functions require at least two enabled axes.",
            arguments[3].span,
        ));
    }
    let smoothing = constant_number(&arguments[6], config)?;
    if !(0.2..=3.0).contains(&smoothing) {
        return Err(FormulaError::expression(
            "invalid_breathing_smoothing",
            &config.id,
            "Breathing smoothing must be between 0.2 and 3 seconds.",
            arguments[6].span,
        ));
    }
    let is_phase = matches!(arguments.len(), 9) && matches!(arguments[7].kind, NodeKind::Number(_));
    let (sensitivity, normalize, invert) = if is_phase {
        let sensitivity = constant_number(&arguments[7], config)?;
        if !(0.0..=1.0).contains(&sensitivity) {
            return Err(FormulaError::expression(
                "invalid_breathing_sensitivity",
                &config.id,
                "Breathing sensitivity must be between 0 and 1.",
                arguments[7].span,
            ));
        }
        (sensitivity, true, constant_boolean(&arguments[8], config)?)
    } else {
        (
            0.6,
            constant_boolean(&arguments[7], config)?,
            constant_boolean(&arguments[8], config)?,
        )
    };
    Ok(ExperimentalBreathingSettings {
        axes,
        smoothing_window_seconds: smoothing as f32,
        sensitivity: sensitivity as f32,
        normalize,
        invert,
    })
}

fn initialize_states(
    node: &Node,
    config: &CustomFormulaConfig,
    states: &mut HashMap<usize, DspState>,
) -> Result<(), FormulaError> {
    match &node.kind {
        NodeKind::Call(name, arguments) => {
            if function_spec(name).is_some_and(|specification| specification.stateful) {
                let state = create_state(name, arguments, config)?;
                states.insert(node.id, state);
            }
            for argument in arguments {
                initialize_states(argument, config, states)?;
            }
        }
        NodeKind::Unary(_, value) => initialize_states(value, config, states)?,
        NodeKind::Binary(_, left, right) => {
            initialize_states(left, config, states)?;
            initialize_states(right, config, states)?;
        }
        NodeKind::Number(_) | NodeKind::Boolean(_) | NodeKind::Variable(_) => {}
    }
    Ok(())
}

fn create_state(
    name: &str,
    arguments: &[Node],
    config: &CustomFormulaConfig,
) -> Result<DspState, FormulaError> {
    let state = match name {
        "delay" => DspState::Delay {
            values: VecDeque::new(),
            samples: duration_capacity(&arguments[1], config)?,
        },
        "moving_mean" | "moving_mean_n" => DspState::Window(WindowState::new(
            if name.ends_with("_n") {
                count_capacity(&arguments[1], config)?
            } else {
                duration_capacity(&arguments[1], config)?
            },
            WindowKind::Mean,
        )),
        "moving_rms" | "moving_rms_n" => DspState::Window(WindowState::new(
            if name.ends_with("_n") {
                count_capacity(&arguments[1], config)?
            } else {
                duration_capacity(&arguments[1], config)?
            },
            WindowKind::Rms,
        )),
        "moving_std" | "moving_std_n" => DspState::Window(WindowState::new(
            if name.ends_with("_n") {
                count_capacity(&arguments[1], config)?
            } else {
                duration_capacity(&arguments[1], config)?
            },
            WindowKind::StandardDeviation,
        )),
        "zscore" | "zscore_n" => DspState::Window(WindowState::new(
            if name.ends_with("_n") {
                count_capacity(&arguments[1], config)?
            } else {
                duration_capacity(&arguments[1], config)?
            },
            WindowKind::ZScore,
        )),
        "ema" => DspState::Ema {
            alpha: 1.0
                - (-1.0 / (config.source.rate_hz() * constant_number(&arguments[1], config)?))
                    .exp(),
            value: None,
        },
        "lowpass" => DspState::LowPass(FirstOrderLowPass::new(
            config.source.rate_hz(),
            validate_cutoff(&arguments[1], config)?,
        )),
        "highpass" => DspState::HighPass(FirstOrderHighPass::new(
            config.source.rate_hz(),
            validate_cutoff(&arguments[1], config)?,
        )),
        "bandpass" => DspState::BandPass {
            high_pass: FirstOrderHighPass::new(
                config.source.rate_hz(),
                validate_cutoff(&arguments[1], config)?,
            ),
            low_pass: FirstOrderLowPass::new(
                config.source.rate_hz(),
                validate_cutoff(&arguments[2], config)?,
            ),
        },
        "derivative" => DspState::Derivative { previous: None },
        "integral" => DspState::Integral {
            previous: None,
            total: 0.0,
        },
        "rmssd" => DspState::Rmssd {
            values: VecDeque::new(),
            samples: count_capacity(&arguments[1], config)?,
        },
        "breathing_magnitude" | "breathing_phase" => DspState::Breathing {
            classifier: ExperimentalBreathingClassifier::new(validate_breathing_arguments(
                arguments, config,
            )?),
            phase: name == "breathing_phase",
        },
        _ => {
            return Err(FormulaError::field(
                "internal_error",
                &config.id,
                "Unknown DSP state.",
            ));
        }
    };
    Ok(state)
}

enum DspState {
    Delay {
        values: VecDeque<f64>,
        samples: usize,
    },
    Window(WindowState),
    Ema {
        alpha: f64,
        value: Option<f64>,
    },
    LowPass(FirstOrderLowPass),
    HighPass(FirstOrderHighPass),
    BandPass {
        high_pass: FirstOrderHighPass,
        low_pass: FirstOrderLowPass,
    },
    Derivative {
        previous: Option<f64>,
    },
    Integral {
        previous: Option<f64>,
        total: f64,
    },
    Rmssd {
        values: VecDeque<f64>,
        samples: usize,
    },
    Breathing {
        classifier: ExperimentalBreathingClassifier,
        phase: bool,
    },
}

#[derive(Clone, Copy)]
enum WindowKind {
    Mean,
    Rms,
    StandardDeviation,
    ZScore,
}

struct WindowState {
    values: VecDeque<f64>,
    capacity: usize,
    sum: f64,
    squared_sum: f64,
    kind: WindowKind,
}

impl WindowState {
    fn new(capacity: usize, kind: WindowKind) -> Self {
        Self {
            values: VecDeque::with_capacity(capacity),
            capacity,
            sum: 0.0,
            squared_sum: 0.0,
            kind,
        }
    }

    fn push(&mut self, value: f64) -> f64 {
        self.values.push_back(value);
        self.sum += value;
        self.squared_sum += value * value;
        if self.values.len() > self.capacity
            && let Some(removed) = self.values.pop_front()
        {
            self.sum -= removed;
            self.squared_sum -= removed * removed;
        }
        let count = self.values.len() as f64;
        let mean = self.sum / count;
        let variance = (self.squared_sum / count - mean * mean).max(0.0);
        match self.kind {
            WindowKind::Mean => mean,
            WindowKind::Rms => (self.squared_sum / count).sqrt(),
            WindowKind::StandardDeviation => variance.sqrt(),
            WindowKind::ZScore => {
                let deviation = variance.sqrt();
                if deviation <= f64::EPSILON {
                    0.0
                } else {
                    (value - mean) / deviation
                }
            }
        }
    }
}

struct FirstOrderLowPass {
    alpha: f64,
    value: Option<f64>,
}

impl FirstOrderLowPass {
    fn new(rate_hz: f64, cutoff_hz: f64) -> Self {
        let dt = 1.0 / rate_hz;
        let rc = 1.0 / (2.0 * PI * cutoff_hz);
        Self {
            alpha: dt / (rc + dt),
            value: None,
        }
    }

    fn push(&mut self, input: f64) -> f64 {
        let output = self
            .value
            .map_or(input, |previous| previous + self.alpha * (input - previous));
        self.value = Some(output);
        output
    }
}

struct FirstOrderHighPass {
    alpha: f64,
    previous_input: Option<f64>,
    previous_output: f64,
}

impl FirstOrderHighPass {
    fn new(rate_hz: f64, cutoff_hz: f64) -> Self {
        let dt = 1.0 / rate_hz;
        let rc = 1.0 / (2.0 * PI * cutoff_hz);
        Self {
            alpha: rc / (rc + dt),
            previous_input: None,
            previous_output: 0.0,
        }
    }

    fn push(&mut self, input: f64) -> f64 {
        let output = self.previous_input.map_or(0.0, |previous_input| {
            self.alpha * (self.previous_output + input - previous_input)
        });
        self.previous_input = Some(input);
        self.previous_output = output;
        output
    }
}

#[derive(Clone, Copy, Debug)]
enum EvalValue {
    Number(f64),
    Boolean(bool),
    NotReady,
}

fn evaluate_node(
    node: &Node,
    frame: FormulaFrame,
    states: &mut HashMap<usize, DspState>,
    budget: &mut usize,
) -> Result<EvalValue, String> {
    if *budget == 0 {
        return Err("Formula operation budget exceeded.".into());
    }
    *budget -= 1;
    match &node.kind {
        NodeKind::Number(value) => Ok(EvalValue::Number(*value)),
        NodeKind::Boolean(value) => Ok(EvalValue::Boolean(*value)),
        NodeKind::Variable(name) => Ok(EvalValue::Number(match name.as_str() {
            "ecg" => frame.ecg,
            "x" => frame.x,
            "y" => frame.y,
            "z" => frame.z,
            "hr" => frame.hr,
            "rr" => frame.rr,
            "pi" => PI,
            "e" => E,
            _ => return Err(format!("Unknown variable '{name}'.")),
        })),
        NodeKind::Unary(operator, value) => {
            let value = evaluate_node(value, frame, states, budget)?;
            match (operator, value) {
                (_, EvalValue::NotReady) => Ok(EvalValue::NotReady),
                (UnaryOp::Positive, EvalValue::Number(value)) => Ok(EvalValue::Number(value)),
                (UnaryOp::Negative, EvalValue::Number(value)) => Ok(EvalValue::Number(-value)),
                (UnaryOp::Not, EvalValue::Boolean(value)) => Ok(EvalValue::Boolean(!value)),
                _ => Err("Unary operator received an incompatible value.".into()),
            }
        }
        NodeKind::Binary(operator, left, right) => {
            evaluate_binary(*operator, left, right, frame, states, budget)
        }
        NodeKind::Call(name, arguments) => {
            evaluate_call(node.id, name, arguments, frame, states, budget)
        }
    }
}

fn evaluate_binary(
    operator: BinaryOp,
    left: &Node,
    right: &Node,
    frame: FormulaFrame,
    states: &mut HashMap<usize, DspState>,
    budget: &mut usize,
) -> Result<EvalValue, String> {
    let left_value = evaluate_node(left, frame, states, budget)?;
    if matches!(left_value, EvalValue::NotReady) {
        return Ok(EvalValue::NotReady);
    }
    if operator == BinaryOp::And && matches!(left_value, EvalValue::Boolean(false)) {
        return Ok(EvalValue::Boolean(false));
    }
    if operator == BinaryOp::Or && matches!(left_value, EvalValue::Boolean(true)) {
        return Ok(EvalValue::Boolean(true));
    }
    let right_value = evaluate_node(right, frame, states, budget)?;
    if matches!(right_value, EvalValue::NotReady) {
        return Ok(EvalValue::NotReady);
    }
    match (left_value, right_value) {
        (EvalValue::Number(left), EvalValue::Number(right)) => Ok(match operator {
            BinaryOp::Add => EvalValue::Number(left + right),
            BinaryOp::Subtract => EvalValue::Number(left - right),
            BinaryOp::Multiply => EvalValue::Number(left * right),
            BinaryOp::Divide => EvalValue::Number(left / right),
            BinaryOp::Modulo => EvalValue::Number(left % right),
            BinaryOp::Power => EvalValue::Number(left.powf(right)),
            BinaryOp::Equal => EvalValue::Boolean(left == right),
            BinaryOp::NotEqual => EvalValue::Boolean(left != right),
            BinaryOp::Greater => EvalValue::Boolean(left > right),
            BinaryOp::GreaterEqual => EvalValue::Boolean(left >= right),
            BinaryOp::Less => EvalValue::Boolean(left < right),
            BinaryOp::LessEqual => EvalValue::Boolean(left <= right),
            BinaryOp::And | BinaryOp::Or => {
                return Err("Logical operators require boolean operands.".into());
            }
        }),
        (EvalValue::Boolean(left), EvalValue::Boolean(right)) => Ok(match operator {
            BinaryOp::And => EvalValue::Boolean(left && right),
            BinaryOp::Or => EvalValue::Boolean(left || right),
            BinaryOp::Equal => EvalValue::Boolean(left == right),
            BinaryOp::NotEqual => EvalValue::Boolean(left != right),
            _ => return Err("Boolean values support only logical and equality operators.".into()),
        }),
        _ => Err("Binary operator received incompatible values.".into()),
    }
}

fn evaluate_call(
    node_id: usize,
    name: &str,
    arguments: &[Node],
    frame: FormulaFrame,
    states: &mut HashMap<usize, DspState>,
    budget: &mut usize,
) -> Result<EvalValue, String> {
    if name == "if" {
        let condition = evaluate_node(&arguments[0], frame, states, budget)?;
        return match condition {
            EvalValue::Boolean(true) => evaluate_node(&arguments[1], frame, states, budget),
            EvalValue::Boolean(false) => evaluate_node(&arguments[2], frame, states, budget),
            EvalValue::NotReady => Ok(EvalValue::NotReady),
            EvalValue::Number(_) => Err("if() condition must be boolean.".into()),
        };
    }
    let mut values = Vec::with_capacity(arguments.len());
    for argument in arguments {
        match evaluate_node(argument, frame, states, budget)? {
            EvalValue::NotReady => return Ok(EvalValue::NotReady),
            value => values.push(value),
        }
    }
    if function_spec(name).is_some_and(|specification| specification.stateful) {
        let input = number(&values[0])?;
        let state = states
            .get_mut(&node_id)
            .ok_or_else(|| "DSP state is unavailable.".to_string())?;
        return evaluate_stateful(state, input, &values, frame.source.rate_hz());
    }
    evaluate_pure(name, &values)
}

fn evaluate_pure(name: &str, values: &[EvalValue]) -> Result<EvalValue, String> {
    let one = || number(&values[0]);
    let two = || -> Result<(f64, f64), String> { Ok((number(&values[0])?, number(&values[1])?)) };
    Ok(EvalValue::Number(match name {
        "abs" => one()?.abs(),
        "sqrt" => one()?.sqrt(),
        "cbrt" => one()?.cbrt(),
        "exp" => one()?.exp(),
        "ln" => one()?.ln(),
        "log10" => one()?.log10(),
        "sin" => one()?.sin(),
        "cos" => one()?.cos(),
        "tan" => one()?.tan(),
        "asin" => one()?.asin(),
        "acos" => one()?.acos(),
        "atan" => one()?.atan(),
        "floor" => one()?.floor(),
        "ceil" => one()?.ceil(),
        "round" => one()?.round(),
        "sign" => one()?.signum(),
        "pow" => {
            let (left, right) = two()?;
            left.powf(right)
        }
        "atan2" => {
            let (left, right) = two()?;
            left.atan2(right)
        }
        "min" => {
            let (left, right) = two()?;
            left.min(right)
        }
        "max" => {
            let (left, right) = two()?;
            left.max(right)
        }
        "clamp" => number(&values[0])?.clamp(number(&values[1])?, number(&values[2])?),
        _ => return Err(format!("Unknown function '{name}'.")),
    }))
}

fn number(value: &EvalValue) -> Result<f64, String> {
    match value {
        EvalValue::Number(value) => Ok(*value),
        EvalValue::Boolean(_) => Err("Numeric argument expected.".into()),
        EvalValue::NotReady => Err("Value is not ready.".into()),
    }
}

fn evaluate_stateful(
    state: &mut DspState,
    input: f64,
    arguments: &[EvalValue],
    rate_hz: f64,
) -> Result<EvalValue, String> {
    let value = match state {
        DspState::Delay { values, samples } => {
            values.push_back(input);
            if values.len() <= *samples {
                return Ok(EvalValue::NotReady);
            }
            values.pop_front().unwrap_or(input)
        }
        DspState::Window(window) => window.push(input),
        DspState::Ema { alpha, value } => {
            let output = value.map_or(input, |previous| previous + *alpha * (input - previous));
            *value = Some(output);
            output
        }
        DspState::LowPass(filter) => filter.push(input),
        DspState::HighPass(filter) => filter.push(input),
        DspState::BandPass {
            high_pass,
            low_pass,
        } => low_pass.push(high_pass.push(input)),
        DspState::Derivative { previous } => {
            let output = previous.map_or(0.0, |previous| (input - previous) * rate_hz);
            *previous = Some(input);
            output
        }
        DspState::Integral { previous, total } => {
            if let Some(previous) = *previous {
                *total += (previous + input) * 0.5 / rate_hz;
            }
            *previous = Some(input);
            *total
        }
        DspState::Rmssd { values, samples } => {
            if (250.0..=2_500.0).contains(&input) {
                values.push_back(input);
                if values.len() > *samples {
                    values.pop_front();
                }
            }
            if values.len() < 2 {
                return Ok(EvalValue::NotReady);
            }
            let mut previous: Option<f64> = None;
            let mut squared_sum = 0.0;
            for value in values.iter().copied() {
                if let Some(previous) = previous {
                    squared_sum += (value - previous).powi(2);
                }
                previous = Some(value);
            }
            (squared_sum / (values.len() - 1) as f64).sqrt()
        }
        DspState::Breathing { classifier, phase } => {
            let sample = AccSample {
                x_mg: number(&arguments[0])?
                    .round()
                    .clamp(i16::MIN as f64, i16::MAX as f64) as i16,
                y_mg: number(&arguments[1])?
                    .round()
                    .clamp(i16::MIN as f64, i16::MAX as f64) as i16,
                z_mg: number(&arguments[2])?
                    .round()
                    .clamp(i16::MIN as f64, i16::MAX as f64) as i16,
            };
            let result = classifier.process(sample);
            if *phase {
                f64::from(result.phase)
            } else {
                f64::from(result.waveform)
            }
        }
    };
    Ok(EvalValue::Number(value))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn formula(source: FormulaSource, expression: &str) -> CustomFormulaConfig {
        CustomFormulaConfig {
            id: "beedcafe-0000-4000-8000-000000000001".into(),
            name: "Test formula".into(),
            source,
            expression: expression.into(),
            unit: "unit".into(),
            enabled: true,
        }
    }

    #[test]
    fn parses_precedence_and_source_variables() {
        let mut compiled = CompiledFormula::compile(formula(
            FormulaSource::Accelerometer,
            "sqrt(x*x + z^2) / 1000",
        ))
        .unwrap();
        let result = compiled.process(FormulaFrame::accelerometer(AccSample {
            x_mg: 300,
            y_mg: 900,
            z_mg: 400,
        }));
        assert!((result.value.unwrap() - 0.5).abs() < 0.0001);
    }

    #[test]
    fn rejects_variables_from_another_clock() {
        let error = CompiledFormula::compile(formula(FormulaSource::Ecg, "ecg + x"))
            .err()
            .unwrap();
        assert_eq!(error.code, "unknown_variable");
        assert!(error.start.is_some());
    }

    #[test]
    fn rejects_non_numeric_results_and_mixed_type_operators() {
        let boolean = CompiledFormula::compile(formula(FormulaSource::Ecg, "ecg > 0"))
            .err()
            .unwrap();
        assert_eq!(boolean.code, "non_numeric_result");

        let mixed = CompiledFormula::compile(formula(FormulaSource::Ecg, "if(ecg, 1, 0)"))
            .err()
            .unwrap();
        assert_eq!(mixed.code, "type_mismatch");
    }

    #[test]
    fn stateful_calls_have_independent_histories() {
        let mut compiled = CompiledFormula::compile(formula(
            FormulaSource::Ecg,
            "moving_mean_n(ecg, 2) - moving_mean_n(ecg * 2, 2)",
        ))
        .unwrap();
        let first = compiled.process(FormulaFrame::ecg(2)).value.unwrap();
        let second = compiled.process(FormulaFrame::ecg(4)).value.unwrap();
        assert_eq!(first, -2.0);
        assert_eq!(second, -3.0);
    }

    #[test]
    fn lazy_if_updates_only_the_selected_dsp_branch() {
        let mut compiled = CompiledFormula::compile(formula(
            FormulaSource::Ecg,
            "if(ecg > 0, moving_mean_n(ecg, 2), moving_mean_n(-ecg, 2))",
        ))
        .unwrap();
        assert_eq!(compiled.process(FormulaFrame::ecg(2)).value, Some(2.0));
        assert_eq!(compiled.process(FormulaFrame::ecg(-8)).value, Some(8.0));
        assert_eq!(compiled.process(FormulaFrame::ecg(4)).value, Some(3.0));
    }

    #[test]
    fn fixed_rate_filters_are_rejected_for_rr() {
        let error = CompiledFormula::compile(formula(FormulaSource::RrInterval, "lowpass(rr, 1)"))
            .err()
            .unwrap();
        assert_eq!(error.code, "fixed_rate_required");
    }

    #[test]
    fn rmssd_matches_the_existing_sixty_sample_definition() {
        let mut compiled =
            CompiledFormula::compile(formula(FormulaSource::RrInterval, "rmssd(rr, 60)")).unwrap();
        assert!(
            compiled
                .process(FormulaFrame::rr_interval(800.0))
                .value
                .is_none()
        );
        assert_eq!(
            compiled.process(FormulaFrame::rr_interval(820.0)).value,
            Some(20.0)
        );
        let value = compiled
            .process(FormulaFrame::rr_interval(780.0))
            .value
            .unwrap();
        assert!((value - 31.622_776).abs() < 0.001);
    }

    #[test]
    fn repeated_non_finite_results_fault_only_the_formula() {
        let mut compiled =
            CompiledFormula::compile(formula(FormulaSource::Ecg, "1 / ecg")).unwrap();
        for _ in 0..9 {
            let result = compiled.process(FormulaFrame::ecg(0));
            assert_ne!(result.state, FormulaRuntimeState::Faulted);
        }
        let fault = compiled.process(FormulaFrame::ecg(0));
        assert_eq!(fault.state, FormulaRuntimeState::Faulted);
        assert_eq!(fault.fault.unwrap().code, "runtime_fault");
    }

    #[test]
    fn rejects_results_outside_the_float32_stream_range() {
        let mut compiled =
            CompiledFormula::compile(formula(FormulaSource::Ecg, "1e100 + ecg")).unwrap();
        for _ in 0..9 {
            assert!(compiled.process(FormulaFrame::ecg(1)).value.is_none());
        }
        let fault = compiled.process(FormulaFrame::ecg(1));
        assert_eq!(fault.state, FormulaRuntimeState::Faulted);
        assert!(
            fault
                .fault
                .unwrap()
                .message
                .contains("float32 stream range")
        );
    }

    #[test]
    fn formula_names_are_protocol_safe() {
        assert_eq!(
            normalize_formula_name("  Filtered ECG / alpha ").unwrap(),
            "Filtered_ECG_alpha"
        );
        assert!(normalize_formula_name("---").is_err());
    }
}
