package com.grotspizzeria.orders;

import java.net.http.HttpClient;
import java.util.function.Supplier;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.http.HttpStatus;
import org.springframework.http.client.JdkClientHttpRequestFactory;
import org.springframework.web.client.ResourceAccessException;
import org.springframework.web.client.RestClient;
import org.springframework.web.client.RestClientResponseException;

import com.grotspizzeria.orders.Models.CheckoutRequest;
import com.grotspizzeria.orders.Models.CheckoutResponse;
import com.grotspizzeria.orders.Models.DeliveryRequest;
import com.grotspizzeria.orders.Models.PriceRequest;
import com.grotspizzeria.orders.Models.PriceResponse;
import com.grotspizzeria.orders.Models.Problem;

/** Thin typed wrappers over the services that orders depends on. */
@Configuration
class DownstreamClients {

    @Bean
    MenuClient menuClient(DownstreamProperties props) {
        return new MenuClient(client(props, props.menuUrl()));
    }

    @Bean
    CheckoutClient checkoutClient(DownstreamProperties props) {
        return new CheckoutClient(client(props, props.checkoutUrl()));
    }

    @Bean
    DeliveryClient deliveryClient(DownstreamProperties props) {
        return new DeliveryClient(client(props, props.deliveryUrl()));
    }

    // The HttpClient lives as long as the application: it is shared by every request the RestClient makes, so it is never closed.
    @SuppressWarnings("PMD.CloseResource")
    private static RestClient client(DownstreamProperties props, String baseUrl) {
        var httpClient = HttpClient.newBuilder()
                .version(HttpClient.Version.HTTP_1_1)
                .connectTimeout(props.connectTimeout())
                .build();
        var requestFactory = new JdkClientHttpRequestFactory(httpClient);
        requestFactory.setReadTimeout(props.readTimeout());
        return RestClient.builder()
                .baseUrl(baseUrl)
                .requestFactory(requestFactory)
                .requestInterceptor(new BearerTokenForwardingInterceptor())
                .build();
    }

    /**
     * Runs a downstream call, translating failures into the problem that this service returns:
     * 400 and 402 are passed through, everything else becomes a 502.
     */
    static <T> T call(String service, Supplier<T> action) {
        try {
            return action.get();
        } catch (RestClientResponseException e) {
            HttpStatus status = HttpStatus.resolve(e.getStatusCode().value());
            Problem problem = bodyOf(e);
            String detail = problem != null && problem.detail() != null
                    ? problem.detail()
                    : "The %s service returned an error.".formatted(service);
            if (status == HttpStatus.BAD_REQUEST || status == HttpStatus.PAYMENT_REQUIRED) {
                String title = problem != null && problem.title() != null ? problem.title() : status.getReasonPhrase();
                throw new ProblemException(status, title, detail, e);
            }
            throw new ProblemException(HttpStatus.BAD_GATEWAY, "Bad gateway",
                    "The %s service failed with status %d: %s".formatted(service, e.getStatusCode().value(), detail), e);
        } catch (ResourceAccessException e) {
            throw new ProblemException(HttpStatus.BAD_GATEWAY, "Bad gateway",
                    "The %s service could not be reached: %s".formatted(service, e.getMessage()), e);
        }
    }

    private static Problem bodyOf(RestClientResponseException e) {
        try {
            return e.getResponseBodyAs(Problem.class);
        } catch (RuntimeException ignored) {
            return null;
        }
    }

    static final class MenuClient {
        private final RestClient client;

        MenuClient(RestClient client) {
            this.client = client;
        }

        PriceResponse price(PriceRequest request) {
            return call("menu", () -> client.post().uri("/menu/price").body(request)
                    .retrieve().body(PriceResponse.class));
        }
    }

    static final class CheckoutClient {
        private final RestClient client;

        CheckoutClient(RestClient client) {
            this.client = client;
        }

        CheckoutResponse charge(CheckoutRequest request) {
            return call("checkout", () -> client.post().uri("/checkout").body(request)
                    .retrieve().body(CheckoutResponse.class));
        }
    }

    static final class DeliveryClient {
        private final RestClient client;

        DeliveryClient(RestClient client) {
            this.client = client;
        }

        void schedule(DeliveryRequest request) {
            call("delivery", () -> client.post().uri("/deliveries").body(request)
                    .retrieve().toBodilessEntity());
        }
    }
}
