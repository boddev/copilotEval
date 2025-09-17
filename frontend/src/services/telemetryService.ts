import { ApplicationInsights } from '@microsoft/applicationinsights-web';

class TelemetryService {
  private appInsights: ApplicationInsights;
  private isInitialized = false;

  constructor() {
    this.appInsights = new ApplicationInsights({
      config: {
        connectionString: import.meta.env.VITE_APP_INSIGHTS_CONNECTION_STRING || '',
        enableAutoRouteTracking: true,
        enableRequestHeaderTracking: true,
        enableResponseHeaderTracking: true,
        enableAjaxErrorStatusText: true,
        enableAjaxPerfTracking: true,
        enableUnhandledPromiseRejectionTracking: true,
        disableFetchTracking: false,
        enableCorsCorrelation: true,
        distributedTracingMode: 2, // W3C Trace Context mode
        maxBatchInterval: 5000,
        maxBatchSizeInBytes: 65536,
        samplingPercentage: 100
      }
    });
  }

  initialize(): void {
    if (!this.isInitialized && this.appInsights.config.connectionString) {
      this.appInsights.loadAppInsights();
      this.appInsights.addTelemetryInitializer((envelope) => {
        envelope.tags = envelope.tags || {};
        envelope.tags['ai.cloud.role'] = 'copiloteval-frontend';
        envelope.tags['ai.cloud.roleInstance'] = window.location.hostname;
        return true;
      });
      this.isInitialized = true;
      console.log('Application Insights initialized for frontend');
    }
  }

  // Track page views
  trackPageView(name?: string, url?: string): void {
    if (this.isInitialized) {
      this.appInsights.trackPageView({
        name: name || document.title,
        uri: url || window.location.href
      });
    }
  }

  // Track custom events
  trackEvent(name: string, properties?: Record<string, any>, measurements?: Record<string, number>): void {
    if (this.isInitialized) {
      this.appInsights.trackEvent({
        name,
        properties: {
          ...properties,
          timestamp: new Date().toISOString(),
          userAgent: navigator.userAgent,
          url: window.location.href
        },
        measurements
      });
    }
  }

  // Track exceptions
  trackException(exception: Error, properties?: Record<string, any>): void {
    if (this.isInitialized) {
      this.appInsights.trackException({
        exception,
        properties: {
          ...properties,
          timestamp: new Date().toISOString(),
          url: window.location.href
        }
      });
    }
  }

  // Track API calls
  trackApiCall(name: string, url: string, duration: number, success: boolean, statusCode?: number, properties?: Record<string, any>): void {
    if (this.isInitialized) {
      this.appInsights.trackDependency({
        target: url,
        name,
        data: url,
        duration,
        success,
        resultCode: statusCode?.toString() || (success ? '200' : '500'),
        type: 'Ajax',
        id: crypto.randomUUID(),
        properties: {
          ...properties,
          timestamp: new Date().toISOString()
        }
      });
    }
  }

  // Track metrics
  trackMetric(name: string, value: number, properties?: Record<string, any>): void {
    if (this.isInitialized) {
      this.appInsights.trackMetric({
        name,
        average: value,
        properties: {
          ...properties,
          timestamp: new Date().toISOString()
        }
      });
    }
  }

  // Track user actions
  trackUserAction(action: string, target?: string, properties?: Record<string, any>): void {
    this.trackEvent('UserAction', {
      action,
      target,
      ...properties
    });
  }

  // Track validation events
  trackValidationEvent(eventType: 'upload' | 'process' | 'complete' | 'error', properties?: Record<string, any>, measurements?: Record<string, number>): void {
    this.trackEvent(`Validation_${eventType}`, {
      component: 'ValidationTable',
      ...properties
    }, measurements);
  }

  // Track performance metrics
  trackPerformance(name: string, duration: number, properties?: Record<string, any>): void {
    this.trackMetric(`performance_${name}`, duration, {
      unit: 'milliseconds',
      ...properties
    });
  }

  // Set user context
  setUser(userId: string, properties?: Record<string, any>): void {
    if (this.isInitialized) {
      this.appInsights.setAuthenticatedUserContext(userId, undefined, true);
      if (properties) {
        Object.entries(properties).forEach(([key, value]) => {
          this.appInsights.addTelemetryInitializer((envelope) => {
            envelope.tags = envelope.tags || {};
            envelope.tags[`ai.user.${key}`] = value;
            return true;
          });
        });
      }
    }
  }

  // Clear user context
  clearUser(): void {
    if (this.isInitialized) {
      this.appInsights.clearAuthenticatedUserContext();
    }
  }

  // Flush telemetry
  flush(): void {
    if (this.isInitialized) {
      this.appInsights.flush();
    }
  }

  // Get correlation context for backend requests
  getCorrelationHeaders(): Record<string, string> {
    if (!this.isInitialized) {
      return {};
    }

    const context = this.appInsights.getPlugin('AjaxPlugin')?.context;
    if (context) {
      return {
        'Request-Id': context.telemetryTrace?.traceID || crypto.randomUUID(),
        'Request-Context': `appId=${this.appInsights.config.instrumentationKey || 'unknown'}`
      };
    }

    return {
      'Request-Id': crypto.randomUUID()
    };
  }
}

// Create singleton instance
export const telemetryService = new TelemetryService();

// Initialize on import if connection string is available
telemetryService.initialize();

export default telemetryService;