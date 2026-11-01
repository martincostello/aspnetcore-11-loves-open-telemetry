import { logs, SeverityNumber } from '@opentelemetry/api';

// A tiny logger: writes a line to stdout and emits an OpenTelemetry log record.
// The log record picks up the trace and span ids of the active context automatically.
const otelLogger = logs.getLogger('grots-menu');

const levels = {
  debug: SeverityNumber.DEBUG,
  info: SeverityNumber.INFO,
  warn: SeverityNumber.WARN,
  error: SeverityNumber.ERROR,
};

function write(level, body, attributes = {}) {
  const stream = level === 'error' ? console.error : console.log;
  const extra = Object.keys(attributes).length > 0 ? ` ${JSON.stringify(attributes)}` : '';
  stream(`${new Date().toISOString()} ${level.toUpperCase().padEnd(5)} ${body}${extra}`);

  otelLogger.emit({
    severityNumber: levels[level],
    severityText: level.toUpperCase(),
    body,
    attributes,
  });
}

export const log = {
  debug: (body, attributes) => write('debug', body, attributes),
  info: (body, attributes) => write('info', body, attributes),
  warn: (body, attributes) => write('warn', body, attributes),
  error: (body, attributes) => write('error', body, attributes),
};
