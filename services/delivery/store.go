package main

import (
	"hash/fnv"
	"sync"
	"time"
)

// The delivery lifecycle. A delivery moves through these statuses, one every stepDuration.
var steps = []step{
	{Status: "preparing", Label: "Preparing your order"},
	{Status: "baking", Label: "Baking in the oven"},
	{Status: "quality-control", Label: "Quality control"},
	{Status: "out-for-delivery", Label: "Out for delivery"},
	{Status: "delivered", Label: "Delivered"},
}

var drivers = []string{"Tempo Tim", "Loki Lou", "Mimir Mia", "Prom Pete", "Pyro Priya", "Alloy Alex", "Beyla Bob"}

// minutesPerStep is used to turn the number of remaining steps into a (made up) ETA.
const minutesPerStep = 9

type step struct {
	Status string `json:"status"`
	Label  string `json:"label"`
}

type stepProgress struct {
	step
	Completed bool      `json:"completed"`
	Reached   bool      `json:"reached"` // the delivery is at, or past, this step
	At        time.Time `json:"at"`      // when the step was reached, or is expected to be
}

// Tracking is the tracking document returned to callers.
type Tracking struct {
	OrderID    string         `json:"orderId"`
	Address    string         `json:"address"`
	Status     string         `json:"status"`
	Driver     string         `json:"driver"`
	ETAMinutes int            `json:"etaMinutes"`
	CreatedAt  time.Time      `json:"createdAt"`
	UpdatedAt  time.Time      `json:"updatedAt"`
	Steps      []stepProgress `json:"steps"`
}

type delivery struct {
	orderID   string
	address   string
	itemCount int
	driver    string
	createdAt time.Time
}

// Store is an in-memory, concurrency-safe collection of deliveries.
type Store struct {
	mu           sync.RWMutex
	deliveries   map[string]*delivery
	stepDuration time.Duration
	now          func() time.Time
}

func NewStore(stepDuration time.Duration) *Store {
	return &Store{
		deliveries:   make(map[string]*delivery),
		stepDuration: stepDuration,
		now:          time.Now,
	}
}

// Create adds a delivery for the order, or returns the existing one. The boolean is true if it was created.
func (s *Store) Create(orderID, address string, itemCount int) (Tracking, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()

	d, exists := s.deliveries[orderID]
	if !exists {
		h := fnv.New32a()
		_, _ = h.Write([]byte(orderID))
		d = &delivery{
			orderID:   orderID,
			address:   address,
			itemCount: itemCount,
			driver:    drivers[int(h.Sum32())%len(drivers)],
			createdAt: s.now().UTC(),
		}
		s.deliveries[orderID] = d
	}
	return s.track(d), !exists
}

// Get returns the current tracking information for an order.
func (s *Store) Get(orderID string) (Tracking, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()

	d, ok := s.deliveries[orderID]
	if !ok {
		return Tracking{}, false
	}
	return s.track(d), true
}

// Active returns the number of deliveries that have not yet been delivered.
func (s *Store) Active() int64 {
	s.mu.RLock()
	defer s.mu.RUnlock()

	var active int64
	for _, d := range s.deliveries {
		if s.index(d) < len(steps)-1 {
			active++
		}
	}
	return active
}

// index derives the current step from the time elapsed since the delivery was created.
func (s *Store) index(d *delivery) int {
	if s.stepDuration <= 0 {
		return len(steps) - 1
	}
	return min(int(s.now().Sub(d.createdAt)/s.stepDuration), len(steps)-1)
}

func (s *Store) track(d *delivery) Tracking {
	current := s.index(d)

	progress := make([]stepProgress, len(steps))
	for i, st := range steps {
		progress[i] = stepProgress{
			step:      st,
			Completed: i < current || current == len(steps)-1,
			Reached:   i <= current,
			At:        d.createdAt.Add(time.Duration(i) * s.stepDuration),
		}
	}

	return Tracking{
		OrderID:    d.orderID,
		Address:    d.address,
		Status:     steps[current].Status,
		Driver:     d.driver,
		ETAMinutes: (len(steps) - 1 - current) * minutesPerStep,
		CreatedAt:  d.createdAt,
		UpdatedAt:  d.createdAt.Add(time.Duration(current) * s.stepDuration),
		Steps:      progress,
	}
}
